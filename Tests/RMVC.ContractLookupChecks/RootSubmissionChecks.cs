using System.Collections.Concurrent;
using System.Reflection;
using RMVC;

namespace RMVC.ContractLookupChecks;

internal static class RootSubmissionChecks
{
    private static readonly ConcurrentQueue<string> events = new();
    private static readonly ConcurrentDictionary<string, (string Id, string Root, string Scope)> roots = new();
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static readonly TaskCompletionSource firstStarted = Signal(), firstRelease = Signal(), exitStarted = Signal(), exitRelease = Signal();
    private static readonly TaskCompletionSource secondStarted = Signal(), separateStarted = Signal(), nestedDone = Signal();
    private static bool? failedExit, cancelledExit;
    private static int nestedResult;
    private static string nestedRoot = "";
    private static void Check(bool condition, string message) => Program.Check(condition, message);
    private static Task Wait(Task task) => task.WaitAsync(TimeSpan.FromSeconds(8));

    internal static async Task Run(string source)
    {
        RFacade.Create<Host>(new Shell(), RFacade.Child<Child>());
        await TestRuntime.WaitIdle();
        Action<RCommandBase[]> submit = source switch
        {
            "model" => Host.Sender.Send,
            "mediator" => Host.Presenter.Send,
            "facade" => Host.Instance.Send,
            "bridge" => Host.Gateway.Send,
            _ => throw new ArgumentException(source)
        };
        string scope = source == "bridge" ? "child" : "host";

        // The source API itself has one params batch overload, not a single-root bypass.
        foreach (var type in new[] { typeof(RCommandExecutorBase), typeof(RFacade) })
        {
            var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(m => m.Name == "Execute" && m.IsFamily).ToArray();
            Check(methods.Length == 1 && methods[0].ReturnType == typeof(void)
                && methods[0].GetParameters().Length == 1
                && methods[0].GetParameters()[0].ParameterType == typeof(RCommandBase[])
                && methods[0].GetParameters()[0].IsDefined(typeof(ParamArrayAttribute)),
                type.Name + " exposes only the params root batch Execute");
            Check(!type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic).Any(m => m.Name == "ExecuteSequentially"),
                type.Name + " has no legacy ExecuteSequentially member");
        }
        RCommandBase[] supplied = [new First(), new Second()];
        submit(supplied);
        await Wait(firstStarted.Task);
        supplied[1] = new MutationProbe();
        Check(!secondStarted.Task.IsCompleted, source + ": second root cannot overtake a running first root");
        submit([new Separate()]);
        await Wait(separateStarted.Task);
        Check(!secondStarted.Task.IsCompleted, source + ": another invocation starts independently of the blocked batch");
        firstRelease.TrySetResult();
        await Wait(exitStarted.Task);
        Check(!secondStarted.Task.IsCompleted, source + ": next root also waits for the preceding exit hook");
        exitRelease.TrySetResult();
        await Wait(secondStarted.Task);
        await TestRuntime.WaitIdle();
        Check(!roots.ContainsKey("mutated"), source + ": submission retains its own batch array");
        Check(events.ToArray().SequenceEqual(new[] { "first-run", "separate-run", "first-exit", "second-run" }),
            source + ": explicit batch preserves run/exit ordering");
        Check(roots["first"].Id == roots["first"].Root && roots["second"].Id == roots["second"].Root
            && roots["first"].Root != roots["second"].Root,
            source + ": batch members retain distinct root identities");
        Check(roots.Values.All(x => x.Scope == scope), source + ": roots use the submitting gateway's destination facade");

        submit([new Failing(), new AfterFailure()]);
        await TestRuntime.WaitIdle();
        Check(failedExit == false && roots.ContainsKey("after-failure"),
            source + ": a failed root does not give its independent successor the failure");
        submit([new CancelSelf(), new AfterCancellation()]);
        await TestRuntime.WaitIdle();
        Check(cancelledExit == false && roots.ContainsKey("after-cancellation"),
            source + ": cancellation of one root does not cancel a later independent batch member");
        submit([new ParentCommand()]);
        await Wait(nestedDone.Task);
        await TestRuntime.WaitIdle();
        Check(nestedResult == 41 && nestedRoot == roots["parent"].Root,
            source + ": nested Execute still awaits a typed result within its parent's root");

        Check(Throws<ArgumentNullException>(() => submit(null!)), source + ": null batch is rejected immediately");
        var valid = new ArgumentProbe();
        Check(Throws<ArgumentException>(() => submit([valid, null!])), source + ": null entry rejects the whole batch before reservation");
        submit([valid]); // the rejected batch must not have reserved this command
        await TestRuntime.WaitIdle();
        Check(roots.ContainsKey("argument"), source + ": a valid command in an invalid batch remains usable");
        submit([]);
        await TestRuntime.WaitIdle();
        Check(true, source + ": an empty batch is a no-op");
        Check(Throws<InvalidOperationException>(() => submit([valid])), source + ": command reuse is still rejected");
    }

    private static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return true; }
        return false;
    }
    // Execution identity is internal; fixture inspection must not add an application API.
    private static (string Id, string Root) Identity(RCommandBase command)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        object tracker = typeof(RCommandBase).GetProperty("Tracker", flags)!.GetValue(command)!;
        return ((string)tracker.GetType().GetProperty("CommandId", flags)!.GetValue(tracker)!,
            (string)tracker.GetType().GetProperty("CommandRootId", flags)!.GetValue(tracker)!);
    }
    private abstract class Probe : RCommand<RVoid>
    {
        protected void Record(string name)
            {
            var identity = Identity(this);
            roots[name] = (identity.Id, identity.Root, Model<Stamp>().Value);
        }
    }
    private sealed class First : Probe
    {
        protected override async Task<RVoid> Run()
        {
            Record("first"); events.Enqueue("first-run"); firstStarted.TrySetResult();
            await firstRelease.Task;
            return await Complete();
        }
        protected override void OnCommandExit(bool success)
        {
            events.Enqueue("first-exit"); exitStarted.TrySetResult();
            // Intentional blocking is confined to this fixture's gated exit-hook check.
            exitRelease.Task.GetAwaiter().GetResult();
        }
    }
    private sealed class Second : Probe
    {
        protected override Task<RVoid> Run()
        { Record("second"); events.Enqueue("second-run"); secondStarted.TrySetResult(); return Complete(); }
    }
    private sealed class Separate : Probe
    {
        protected override Task<RVoid> Run()
        { Record("separate"); events.Enqueue("separate-run"); separateStarted.TrySetResult(); return Complete(); }
    }
    private sealed class Failing : Probe
    {
        protected override Task<RVoid> Run() => throw new IOException("first root failed");
        protected override void OnCommandExit(bool success) => failedExit = success;
    }
    private sealed class AfterFailure : Probe
    {
        protected override Task<RVoid> Run() { Record("after-failure"); return Complete(); }
    }
    private sealed class CancelSelf : Probe
    {
        protected override Task<RVoid> Run() { AbortAllCommands(); return Complete(); }
        protected override void OnCommandExit(bool success) => cancelledExit = success;
    }
    private sealed class AfterCancellation : Probe
    {
        protected override Task<RVoid> Run() { Record("after-cancellation"); return Complete(); }
    }
    private sealed class ParentCommand : Probe
    {
        protected override async Task<RVoid> Run()
        { Record("parent"); nestedResult = await Execute(new TypedChild()); nestedDone.TrySetResult(); return await Complete(); }
    }
    private sealed class TypedChild : RCommand<int>
    {
        protected override Task<int> Run() { nestedRoot = Identity(this).Root; return Task.FromResult(41); }
    }
    private sealed class MutationProbe : Probe
    {
        protected override Task<RVoid> Run() { Record("mutated"); return Complete(); }
    }
    private sealed class ArgumentProbe : Probe
    {
        protected override Task<RVoid> Run() { Record("argument"); return Complete(); }
    }
    public sealed class Stamp(string value) : RModel { internal string Value => value; }
    public sealed class Sender : RModel { internal void Send(params RCommandBase[] commands) => Execute(commands); }
    public interface IDisplay : IRContract { }
    public sealed class Presenter : RMediator<IDisplay>
    {
        internal void Send(params RCommandBase[] commands) => Execute(commands);
        protected override void ActorAttached(IDisplay actor) { }
        protected override void ActorDetached(IDisplay actor) { }
    }
    public sealed class Gateway : RBridge<Child>
    {
        internal void Send(params RCommandBase[] commands) => Execute(commands);
    }
    public sealed class Host : RFacade
    {
        internal static Host Instance = null!;
        internal static readonly Sender Sender = new();
        internal static readonly Presenter Presenter = new();
        internal static readonly Gateway Gateway = new();
        public Host() => Instance = this;
        internal void Send(params RCommandBase[] commands) => Execute(commands);
        protected override IRModel[] RegisterModels() => [Sender, new Stamp("host")];
        protected override RMediator[] RegisterMediators() => [Presenter];
        protected override RBridge[] RegisterBridges() => [Gateway];
        protected override RCommandBase CreateInitialiseCommand(IRInitialisation? payload) => new HostInit();
    }
    public sealed class Child : RFacade
    {
        protected override IRModel[] RegisterModels() => [new Stamp("child")];
        protected override RMediator[] RegisterMediators() => [];
        protected override RCommandBase CreateInitialiseCommand(IRInitialisation? payload) => new ChildInit();
    }
    private sealed class HostInit : RCommand<RVoid>
    {
        protected override Task<RVoid> Run() { InitialiseFacadeChild<Child>(); return Complete(); }
    }
    private sealed class ChildInit : RCommand<RVoid> { protected override Task<RVoid> Run() => Complete(); }
    private sealed class Shell : IRShell
    {
        public void HandleStateChanged(RApplicationState state, string? message = null)
        { if (state == RApplicationState.Failed) throw new Exception(message); }
        public bool ShowMessageBox(string title, string message, bool isYesNo = false) => false;
    }
}
