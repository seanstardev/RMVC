using System.Reflection;
using System.Collections;
using RMVC;

namespace RMVC.ContractLookupChecks;

internal static class InitialisationCleanupChecks
{
    private static string scenario = "";
    private static readonly object gate = new();
    private static readonly List<string> events = new();
    private static readonly TaskCompletionSource entered = Signal(), release = Signal(), cleanupEntered = Signal(), cleanupRelease = Signal(), allCleaned = Signal();
    private static readonly TestShell shell = new();
    private static int cleaned;
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Record(string value) { lock (gate) events.Add(value); }
    private static string[] History() { lock (gate) return events.ToArray(); }
    private static void Check(bool condition, string message) => Program.Check(condition, scenario + ": " + message);

    internal static async Task Run(string mode)
    {
        scenario = mode;
        RFacade.Create<Root>(shell, RFacade.Child<First>(), RFacade.Child<Second>());
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(8));
        var view = new View();
        RFacade.RegisterActor<Root, IView>(view);
        Root.Instance.Send(new Probe("queued-root"));
        First.Instance.Send(new Probe("queued-first"));
        Second.Instance.Send(new Probe("queued-second"));
        if (mode == "cancel") RFacade.AbortAllCommands();
        release.TrySetResult();

        if (mode == "success")
        {
            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (History().Count(e => e.StartsWith("queued-")) < 3 && DateTime.UtcNow < deadline) await Task.Delay(5);
            Check(History().Count(e => e.StartsWith("queued-")) == 3, "successful composition releases all ordinary queues");
            Check(cleaned == 0 && shell.Failures == 0, "success does not trigger final cleanup");
            return;
        }

        if (mode == "async-block")
        {
            await cleanupEntered.Task.WaitAsync(TimeSpan.FromSeconds(8));
            Check(shell.Failures == 0, "failure notification awaits outstanding cleanup");
            Check(!History().Any(e => e.StartsWith("end:")), "later cleanup cannot overtake the first asynchronous cleanup");
            Check(Rejected(Root.Instance) && Rejected(First.Instance) && Rejected(Second.Instance),
                "every facade rejects ordinary work before cleanup begins");
            Task<IReadOnlyList<Exception>> first = CleanupTask(Second.Instance);
            Check(ReferenceEquals(first, CleanupTask(Second.Instance)) && !first.IsCompleted,
                "overlapping cleanup requests share one pending task");
            cleanupRelease.TrySetResult();
            await first.WaitAsync(TimeSpan.FromSeconds(8));
        }

        await allCleaned.Task.WaitAsync(TimeSpan.FromSeconds(8));
        await shell.Failed.Task.WaitAsync(TimeSpan.FromSeconds(8));
        await CleanupTask(Root.Instance).WaitAsync(TimeSpan.FromSeconds(8));
        string[] expected = ["Second:B", "Second:A", "First:B", "First:A", "Root:B", "Root:A"];
        Check(History().Where(e => e.StartsWith("start:")).Select(e => e[6..]).SequenceEqual(expected),
            "facades and models clean up sequentially in reverse registration order");
        Check(cleaned == 6 && History().Count(e => e.StartsWith("end:")) == 6, "each registered model gets one cleanup opportunity");
        Check(!History().Any(e => e.StartsWith("queued-")), "failed composition never releases its queued work");
        Check(Rejected(Root.Instance) && Rejected(First.Instance) && Rejected(Second.Instance),
            "failed composition stays closed after cleanup");
        await CleanupTask(Root.Instance); await CleanupTask(First.Instance); await CleanupTask(Second.Instance);
        Check(cleaned == 6, "later cleanup requests do not repeat model cleanup");
        Check(Root.Instance.Presenter.Attached(view) && Root.Instance.Presenter.Detaches == 0 && !view.Disposed,
            "model cleanup does not detach or dispose attached objects");
        if (mode is "root-failure" or "cancel")
            Check(!History().Contains("init:First") && !History().Contains("init:Second")
                && History().Contains("state:Second:B:False"), "unstarted child models still receive cleanup");
        if (mode is "first-failure" or "factory-failure")
            Check(!History().Contains("init:Second") && History().Contains("state:Second:A:False"),
                "a child failure cleans up later uninitialised children too");
        string owner = mode is "root-failure" or "cancel" ? typeof(Root).FullName! :
            mode is "first-failure" or "factory-failure" ? typeof(First).FullName! : typeof(Second).FullName!;
        Check(shell.Failures == 1 && shell.FacadeName == owner, "one notification retains the original failing facade");
        Check(shell.Message.StartsWith(mode == "cancel" ? "Facade '" : "original failure"),
            "original initialisation failure remains the leading diagnostic");
    
        if (mode == "cleanup-errors")
        {
            Check(shell.Message.Contains(typeof(OwnedModel).FullName!) && shell.Message.Contains(typeof(PlainModel).FullName!)
                && shell.Message.Contains(typeof(First).FullName!) && shell.Message.Contains(typeof(Second).FullName!)
                && shell.Message.Split("Cleanup failed for model").Length == 5
                && shell.Message.Contains("Cleanup() returned null"), "all cleanup failures carry model/facade context: " + shell.Message);
            try { Root.Instance.Send(new Probe("forbidden")); }
            catch (InvalidOperationException error)
            {
                Check(error.InnerException is AggregateException aggregate && aggregate.InnerExceptions.Count == 5
                    && aggregate.InnerExceptions[0].Message == "original failure",
                    "stored failure preserves original cause separately from four cleanup errors");
            }
        }
        if (mode == "no-shell") Check(shell.Failures == 0, "cleanup completes without a Shell");
        if (mode == "recreate-rejected")
        {
            Check(Program.ThrowsLookup(() => RFacade.Create<Other>(new Shell()), "only once"),
                "completed failure cleanup does not restore creation eligibility");
        }
    }

    private static bool Rejected(Scope facade) => Program.ThrowsLookup(() => facade.Send(new Probe("forbidden")), "startup failed");
    private static Task<IReadOnlyList<Exception>> CleanupTask(RFacade facade)
    {
        object actors = typeof(RFacade).GetField("actors", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(facade)!;
        return (Task<IReadOnlyList<Exception>>)actors.GetType().GetMethod("CleanupModels", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(actors, null)!;
    }
    private static async Task Cleanup(string name, bool initialised)
    {
        Record("start:" + name); Record("state:" + name + ":" + initialised);
        if (scenario == "async-block" && name == "Second:B") { cleanupEntered.TrySetResult(); await cleanupRelease.Task; }
        else await Task.Delay(2);
        Record("end:" + name);
        if (Interlocked.Increment(ref cleaned) == 6) allCleaned.TrySetResult();
        if (scenario == "cleanup-errors")
        {
            if (name == "Second:A") throw new ApplicationException("asynchronous cleanup failure");
            if (name == "First:B") throw new OperationCanceledException("cleanup was cancelled");
        }
    }
    public sealed class OwnedModel : RModel
    {
        internal bool Initialised;
        private readonly string name;
        internal OwnedModel(string name) => this.name = name;
        public override Task Cleanup() => InitialisationCleanupChecks.Cleanup(name, Initialised);
    }
    public sealed class PlainModel : IRModel
    {
        internal bool Initialised;
        private readonly string name;
        internal PlainModel(string name) => this.name = name;
        public Task Cleanup()
        {
            if (scenario == "cleanup-errors" && name is "Second:B" or "Root:B")
            {
                Record("start:" + name); Record("state:" + name + ":" + Initialised); Record("end:" + name);
                if (Interlocked.Increment(ref cleaned) == 6) allCleaned.TrySetResult();
                if (name == "Second:B") throw new ApplicationException("synchronous cleanup failure");
                return null!;
            }
            return InitialisationCleanupChecks.Cleanup(name, Initialised);
        }
    }
    public abstract class Scope : RFacade
    {
        private readonly OwnedModel first;
        private readonly PlainModel second;
        internal readonly Presenter Presenter = new();
        internal Scope() { first = new(GetType().Name + ":A"); second = new(GetType().Name + ":B"); }
        internal void Send(RCommandBase command) => Execute(command);
        protected override IRModel[] RegisterModels() => [first, second];
        protected override RMediator[] RegisterMediators() => [Presenter];
        protected override RCommandBase CreateInitialiseCommand(IRInitialisation? payload)
        {
            if (scenario == "factory-failure" && this is First) throw new InvalidOperationException("original failure");
            return new Initialise(GetType().Name);
        }
    }
    public sealed class Root : Scope { internal static Root Instance = null!; public Root() => Instance = this; }
    public sealed class First : Scope { internal static First Instance = null!; public First() => Instance = this; }
    public sealed class Second : Scope { internal static Second Instance = null!; public Second() => Instance = this; }
    public sealed class Other : Scope { internal static Other Instance = null!; public Other() => Instance = this; }
    private sealed class Initialise(string name) : RCommand<RVoid>
    {
        protected override async Task<RVoid> Run()
        {
            Record("init:" + name);
            if (name == "Root") { entered.TrySetResult(); await release.Task; ThrowIfAbortRequested(); }
            Model<OwnedModel>().Initialised = true;
            if (name == "Root" && scenario is "root-failure") throw new InvalidOperationException("original failure");
            if (name == "First" && scenario == "first-failure") throw new InvalidOperationException("original failure");
            Model<PlainModel>().Initialised = true;
            if (name == "Root") { InitialiseFacadeChild<First>(); InitialiseFacadeChild<Second>(); }
            if (name == "Second" && scenario is not "success" and not "cancel" and not "root-failure" and not "first-failure" and not "factory-failure")
                throw new InvalidOperationException("original failure");
            return await Complete();
        }
    }
    private sealed class Probe(string name) : RCommand<RVoid>
    {
        internal readonly TaskCompletionSource Done = Signal();
        protected override Task<RVoid> Run() { Record(name); Done.TrySetResult(); return Complete(); }
    }
    public interface IView : IRContract { }
    private sealed class View : IView, IDisposable { internal bool Disposed; public void Dispose() => Disposed = true; }
    public sealed class Presenter : RMediator<IView>
    {
        internal int Detaches;
        internal bool Attached(IView actor) => ReferenceEquals(Actor, actor);
        protected override void ActorAttached(IView actor) { }
        protected override void ActorDetached(IView actor) => Detaches++;
    }
    private sealed class TestShell : IRShell
    {
        internal readonly TaskCompletionSource Failed = Signal();
        internal int Failures;
        internal string Message = "", FacadeName = "";
        public void HandleStateChanged(RMVC.RApplicationState state, string? detail = null)
        {
            if (state != RMVC.RApplicationState.Failed) return;
            string message = detail ?? "";
            string name = message.StartsWith("Facade: ") ? message.Split('\n')[0][8..].Trim() : "";
            if (message.StartsWith("Facade: ")) message = message[(message.IndexOf('\n') + 1)..];
FacadeName = name; Message = message; Interlocked.Increment(ref Failures); Failed.TrySetResult();
            if (scenario == "throwing-shell") throw new InvalidOperationException("notification failed");
        }
        public bool ShowMessageBox(string title, string message, bool isYesNo = false) => false;
    }
}
