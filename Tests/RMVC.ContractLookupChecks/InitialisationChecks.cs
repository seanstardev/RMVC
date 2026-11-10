using System.Reflection;
using RMVC;

namespace RMVC.ContractLookupChecks;

internal static class InitialisationChecks
{
    private static string scenario = "";
    private static readonly object gate = new();
    private static readonly List<string> events = new();
    private static readonly TaskCompletionSource entered = Signal();
    private static readonly TaskCompletionSource release = Signal();
    private static readonly TaskCompletionSource childEntered = Signal();
    private static readonly TaskCompletionSource childRelease = Signal();
    private static readonly TaskCompletionSource queuesDone = Signal();
    private static readonly TestShell shell = new();
    private static int probes;
    private static bool progressRejected;
    private static readonly TaskCompletionSource unrelatedRun = Signal();
    private static ParentInit? parentCommand;
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Record(string value) { lock (gate) events.Add(value); }
    private static string[] Events() { lock (gate) return events.ToArray(); }
    private static void Check(bool value, string message) => Program.Check(value, scenario + ": " + message);

    internal static async Task Run(string mode)
    {
        scenario = mode;
        if (mode == "unbound")
        {
            var command = new ParentInit();
            Check(Program.ThrowsLookup(command.StageFirst, "bound"), "unbound staging throws");
            Check(Program.ThrowsLookup(() => command.ReadRole(), "bound"), "unbound role access throws");
            return;
        }
        Check(typeof(RFacade).GetMethod("Initialise") == null, "public direct initialisation is absent");
        Check(typeof(RCommandBase).GetMethod("GetFacadeChild", BindingFlags.NonPublic | BindingFlags.Instance) == null,
            "old child accessor is absent");
        RFacade.Create<Root>(shell, RFacade.Child<First>(), RFacade.Child<Second>());
        await Task.WhenAny(entered.Task, shell.Failed.Task).WaitAsync(TimeSpan.FromSeconds(8));
        if (entered.Task.IsCompleted)
        {
            Root.Instance.Send(new Probe("root-queue"));
            First.Instance.Send(new Probe("first-queue"));
            Second.Instance.Send(new Probe("second-queue"));
            if (mode == "early-queued-init")
            {
                First.Instance.Send(new ChildInit("forbidden", new Payload()));
                First.Instance.Send(new Probe("after-forbidden"));
            }
            release.TrySetResult();
        }

        if (mode is "cancel-child" or "late" or "cancel-parent")
        {
            if (mode == "cancel-parent")
                RFacade.AbortAllCommands();
            else
            {
                await childEntered.Task.WaitAsync(TimeSpan.FromSeconds(8));
                Check(!Events().Any(e => e.EndsWith("-queue")), "ordinary queues remain blocked during child init");
                if (mode == "late")
                {
                    Check(Program.ThrowsLookup(parentCommand!.StageFirst, "selected initialisation"), "late staging throws");
                    Check(Program.ThrowsLookup(() => Root.Instance.Send(parentCommand!), "ordinary execution"),
                        "selected initialiser cannot be resubmitted normally");
                    childRelease.TrySetResult();
                }
                else
                    typeof(RFacade).GetMethod("AbortCommands",
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
                        .MakeGenericMethod(typeof(Root)).Invoke(null, null);
            }
        }

        bool success = mode is "success" or "late" or "progress" or "ordinary-rejection" or "early-queued-init" or "type-scope";
        if (success)
        {
            await queuesDone.Task.WaitAsync(TimeSpan.FromSeconds(8));
            var history = Events();
            Check(shell.Failures == 0, "success has no failure notification");
            Check(Array.IndexOf(history, "parent-exit") < Array.IndexOf(history, "first-run")
                && Array.IndexOf(history, "first-exit") < Array.IndexOf(history, "second-run"),
                "parent exit and registered child order are respected");
            Check(history.TakeWhile(e => e != "second-exit").All(e => !e.EndsWith("-queue")),
                "all initialisers finish before any queue drains");
            Check(!shell.EarlyIdle, "there is no idle gap during initialisation");
            if (mode == "progress") Check(progressRejected, "progress callback staging is rejected");
            if (mode == "early-queued-init")
            {
                var deadline = DateTime.UtcNow.AddSeconds(8);
                while (!Events().Contains("after-forbidden") && DateTime.UtcNow < deadline) await Task.Delay(5);
                Check(Events().Contains("after-forbidden") && !Events().Contains("forbidden-run"),
                    "queued instances are rejected after the initialiser type becomes known");
            }
            if (mode == "type-scope")
            {
                var command = new ParentInit();
                First.Instance.Send(command);
                await unrelatedRun.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Check(Events().Contains("unrelated-run"), "initialiser type restriction belongs to its owning facade");
            }
            if (mode == "ordinary-rejection")
            {
                Check(Program.ThrowsLookup(() => Root.Instance.Send(new ParentInit()), "ordinary execution"),
                    "ordinary roots reject another instance of the initialiser type");
                var normal = new Probe("sequence-survivor");
                Check(Program.ThrowsLookup(() => Root.Instance.Sequence(normal, new ParentInit()), "ordinary execution"),
                    "sequences reject the initialiser type before reserving other instances");
                Root.Instance.Send(normal);
                await normal.Done.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        else
        {
            await shell.Failed.Task.WaitAsync(TimeSpan.FromSeconds(8));
            Check(shell.Failures == 1, "one failure notification per composition");
            Check(!Events().Any(e => e.EndsWith("-queue")), "failure never releases ordinary queues");
            Check(Program.ThrowsLookup(() => Root.Instance.Send(new Probe("forbidden-queue")), "startup failed")
                && Program.ThrowsLookup(() => First.Instance.Send(new Probe("forbidden-queue")), "startup failed"),
                "later roots are rejected across the failed composition");
            if (mode is "missing" or "duplicate" or "wrong-target" or "self" or "parent-error"
                or "exit" or "title" or "helper" or "nested-init" or "cancel-parent" or "reuse-command")
                Check(!Events().Contains("first-run") && !Events().Contains("second-run"), "no child began");
            if (mode is "child-error" or "cancel-child" or "cancel-between" or "wrong-payload" or "null-payload")
                Check(!Events().Contains("second-run"), "later child did not begin");
            string expected = mode switch
            {
                "missing" => "did not schedule",
                "duplicate" => "already scheduled",
                "wrong-target" or "self" => "no direct child",
                "helper" or "exit" or "title" => "selected initialisation",
                "nested-init" or "nested-init-untyped" or "progress-initialiser" => "ordinary execution",
                "null-factory" => "returned null",
                "wrong-payload" or "null-payload" => "requires Payload",
                "parent-error" => "parent failed",
                "child-error" => "child failed",
                "reuse-command" => "reserved",
                _ => ""
            };
            Check(shell.Message.Contains(expected), "failure retains its useful diagnostic: " + shell.Message);
            if (mode is "child-error" or "wrong-payload" or "null-payload" or "cancel-child" or "cancel-between")
                Check(shell.FacadeName == typeof(First).FullName, "notification identifies the actual failing child");
            if (mode == "recreate-rejected")
            {
                Check(Program.ThrowsLookup(() => RFacade.Create<Independent>(shell), "only once"),
                    "failed initialisation does not permit a new root environment");
            }
        }
    }

    public sealed class Payload : IRInitialisation { }
    public sealed class WrongPayload : IRInitialisation { }
    public abstract class Scope : RFacade
    {
        internal void Send(RCommandBase command) => Execute(command);
        internal void Sequence(params RCommandBase[] commands) => Execute(commands);
        protected override IRModel[] RegisterModels() => [];
        protected override RMediator[] RegisterMediators() => [];
    }
    public sealed class Independent : Scope
    {
        internal static Independent Instance = null!;
        public Independent() => Instance = this;
        protected override RCommandBase CreateInitialiseCommand(IRInitialisation? payload) => new IndependentInit();
    }
    private sealed class IndependentInit : RCommand<RVoid>
    {
        protected override Task<RVoid> Run() { Record("independent-init"); return Complete(); }
    }
    public sealed class Root : Scope
    {
        internal static Root Instance = null!;
        public Root() => Instance = this;
        protected override RCommandBase CreateInitialiseCommand(IRInitialisation? initialisation)
        {
            if (scenario == "null-factory") return null!;

            return parentCommand = new ParentInit();
        }
        protected override RCommandBase? CreateProgressUpdateCommand(string id, RProgress[] progress)
            => scenario == "progress-initialiser" ? new ParentInit() : new ProgressProbe();
    }
    public sealed class First : Scope
    {
        internal static First Instance = null!;
        public First() => Instance = this;
        protected override RCommandBase CreateInitialiseCommand(IRInitialisation? initialisation)
            => scenario == "reuse-command" ? parentCommand! : new ChildInit("first", initialisation);
    }
    public sealed class Second : Scope
    {
        internal static Second Instance = null!;
        public Second() => Instance = this;
        protected override RCommandBase CreateInitialiseCommand(IRInitialisation? initialisation)
            => new ChildInit("second", initialisation);
    }
    private sealed class ParentInit : RCommand<RVoid>
    {
        internal void StageFirst() => InitialiseFacadeChild<First>(new Payload());
        internal bool ReadRole() => HasFacadeParent;
        protected override string GetProgressTitle()
        {
            if (scenario == "title") StageFirst();
            return base.GetProgressTitle();
        }
        protected override async Task<RVoid> Run()
        {
            if (scenario == "type-scope" && HasFacadeParent)
            {
                Record("unrelated-run"); unrelatedRun.TrySetResult(); return await Complete();
            }
            Check(!HasFacadeParent, "root role comes from composition");
            Record("parent-run"); entered.TrySetResult(); await release.Task;
            if (scenario == "cancel-parent")
            {
                while (true) { ThrowIfAbortRequested(); await Task.Delay(5); }
            }
            if (scenario == "wrong-target") InitialiseFacadeChild<RFacade>();
            if (scenario == "self") InitialiseFacadeChild<Root>();
            if (scenario == "helper") { await Execute(new Helper()); return await Complete(); }
            if (scenario == "nested-init") await Execute(new ParentInit());
            if (scenario == "nested-init-untyped") await Execute((RCommandBase)new ParentInit());
            if (scenario is "progress" or "progress-initialiser") await SetProgress(50, "preparing");
            if (scenario != "missing")
                InitialiseFacadeChild<Second>(); // Deliberately reverse request order.
            InitialiseFacadeChild<First>(scenario == "wrong-payload" ? new WrongPayload()
                : scenario == "null-payload" ? null : new Payload());
            if (scenario == "duplicate") InitialiseFacadeChild<First>();
            if (scenario is "parent-error" or "missing-shell" or "throwing-shell" or "recreate-rejected") SetError("parent failed");
            return await Complete();
        }
        protected override void OnCommandExit(bool success)
        {
            Record("parent-exit");
            if (scenario == "exit") StageFirst();
        }
    }
    private sealed class ChildInit : RCommand<RVoid>
    {
        private readonly string name;
        private readonly IRInitialisation? payload;
        internal ChildInit(string name, IRInitialisation? payload) { this.name = name; this.payload = payload; }
        protected override async Task<RVoid> Run()
        {
            Record(name + "-run");
            Check(HasFacadeParent, "null payload does not change child role");
            if (name == "first")
            {
                if (payload is not Payload) throw new InvalidOperationException("first requires Payload");
                if (scenario == "child-error") throw new InvalidOperationException("child failed");
                if (scenario is "cancel-child" or "late")
                {
                    childEntered.TrySetResult();
                    while (!childRelease.Task.IsCompleted) { ThrowIfAbortRequested(); await Task.Delay(5); }
                }
            }
            else Check(payload == null, "explicit null payload is a scheduled request");
            return await Complete();
        }
        protected override void OnCommandExit(bool success)
        {
            Record(name + "-exit");
            if (name == "first" && scenario == "cancel-between") AbortCommands<Root>();
        }
    }
    private sealed class Helper : RCommand<RVoid>
    {
        protected override Task<RVoid> Run() { InitialiseFacadeChild<First>(); return Complete(); }
    }
    private sealed class ProgressProbe : RCommand<RVoid>
    {
        protected override Task<RVoid> Run()
        {
            progressRejected |= Program.ThrowsLookup(() => InitialiseFacadeChild<First>(), "selected initialisation");
            return Complete();
        }
    }
    private sealed class Probe : RCommand<RVoid>
    {
        private readonly string name;
        internal readonly TaskCompletionSource Done = Signal();
        internal Probe(string name) => this.name = name;
        protected override Task<RVoid> Run()
        {
            Record(name); Done.TrySetResult();
            if (Interlocked.Increment(ref probes) == 3) queuesDone.TrySetResult();
            return Complete();
        }
    }
    private sealed class TestShell : IRShell
    {
        internal readonly TaskCompletionSource Failed = Signal();
        internal int Failures;
        internal string Message = "";
        internal string FacadeName = "";
        internal bool EarlyIdle;
        public void HandleStateChanged(RMVC.RApplicationState state, string? detail = null)
        {
            if (state == RMVC.RApplicationState.Running) VerifyRunningBoundary();
            if (state != RMVC.RApplicationState.Failed) return;
            string message = detail ?? "";
            string name = message.StartsWith("Facade: ") ? message.Split('\n')[0][8..].Trim() : "";
            if (message.StartsWith("Facade: ")) message = message[(message.IndexOf('\n') + 1)..];
FacadeName = name; Message = message; Interlocked.Increment(ref Failures); Failed.TrySetResult();
            if (scenario == "throwing-shell") throw new InvalidOperationException("notification failed");
        }
        public void VerifyRunningBoundary()
        {
            if (scenario is "success" or "late" or "progress" or "ordinary-rejection" or "early-queued-init" or "type-scope")
                EarlyIdle |= !Events().Contains("second-exit");
        }
        public bool ShowMessageBox(string title, string message, bool isYesNo = false) => false;
    }
}
