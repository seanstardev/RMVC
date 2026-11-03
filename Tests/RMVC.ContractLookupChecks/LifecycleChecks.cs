using System.Collections.Concurrent;
using System.Reflection;
using RMVC;

namespace RMVC.ContractLookupChecks;

internal static class LifecycleChecks
{
    private static string mode = "";
    private static readonly TaskCompletionSource started = Signal(), release = Signal(), cleaned = Signal();
    private static readonly ConcurrentQueue<string> cleanupOrder = new();
    private static int cleanups, secondRan;
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Check(bool value, string message) => Program.Check(value, mode + ": " + message);
    private static Task Wait(Task task) => task.WaitAsync(TimeSpan.FromSeconds(6));

    internal static async Task Run(string scenario)
    {
        mode = scenario;
        var shell = new HostShell();
        if (mode == "null-shell")
        {
            bool rejected = false;
            try { RFacade.Create<Root>(null!); } catch (ArgumentNullException) { rejected = true; }
            Check(rejected && Root.Constructors == 0, "null Shell is rejected before constructors");
            Check(Program.ThrowsLookup(() => RFacade.Create<Root>(shell), "only once"), "invalid first call still consumes Create");
            return;
        }
        if (mode == "constructor-stall")
        {
            var creation = Task.Run(() => RFacade.Create<Root>(shell));
            await Wait(started.Task);
            _ = RFacade.Shutdown(70);
            await Wait(shell.Terminal.Task);
            Check(shell.Terminal.Task.Result == RApplicationState.Failed && cleanups == 0,
                "constructor stall expires without racing model cleanup");
            release.TrySetResult();
            await Wait(creation);
            await Wait(cleaned.Task);
        }
        else
        {
            RFacade.Create<Root>(shell, RFacade.Child<Child>());
            if (mode == "startup-failure")
            {
                await Wait(shell.Terminal.Task);
                Check(shell.Message.Contains("Startup stage:") && shell.Message.Contains("original startup fault"),
                    "startup failure preserves original cause and stage in Failed");
                Check(cleanups == 2, "startup Failed follows all eligible cleanup");
                _ = RFacade.Shutdown(1000);
                Check(shell.States.Count == 2, "Shutdown cannot retry a terminal startup failure");
                return;
            }
            if (mode == "startup-cleanup-timeout")
            {
                await Wait(started.Task);
                _ = RFacade.Shutdown(70);
                await Wait(shell.Terminal.Task);
                Check(cleanups == 1, "shutdown deadline does not repeat an in-progress startup recovery cleanup");
                release.TrySetResult();
                await Wait(cleaned.Task);
            }
            else if (mode == "from-command")
            {
                await Wait(shell.Ready.Task);
                Root.Instance.Send(new Stopper());
                await Wait(shell.Terminal.Task);
            }
            else if (mode == "initialisation-stall")
            {
                await Wait(started.Task);
                _ = RFacade.Shutdown(70);
                await Wait(shell.Terminal.Task);
                Check(cleanups == 0, "initialisation still using resources is not cleaned prematurely");
                release.TrySetResult();
                await Wait(cleaned.Task);
            }
            else if (mode is "reentrant-initialising" or "reentrant-running")
                await Wait(shell.Terminal.Task);
            else
            {
                await Wait(shell.Ready.Task);
                await TestRuntime.WaitIdle();
                Check(shell.ShowMessageBox("test", "yes", true), "Shell decision is synchronous");
                if (mode == "invalid-budget")
                {
                    bool rejected = false;
                    try { _ = RFacade.Shutdown(uint.MaxValue); } catch (ArgumentOutOfRangeException) { rejected = true; }
                    Check(rejected && shell.States.Last() == RApplicationState.Running, "invalid budget leaves the live application intact");
                }
                if (mode is "active" or "command-stall" or "cancel-callback-stall" or "queue")
                {
                    Root.Instance.SubmitWork();
                    await Wait(started.Task);
                }
                _ = RFacade.Shutdown(mode is "command-stall" or "cancel-callback-stall" or "cleanup-sync-stall" or "cleanup-async-stall" ? 70u : mode == "zero" ? 0u : 3000u);
                if (mode is "command-stall" or "cancel-callback-stall" or "cleanup-sync-stall" or "cleanup-async-stall")
                {
                    await Wait(shell.Terminal.Task);
                    Check(shell.Terminal.Task.Result == RApplicationState.Failed, "deadline reports Failed");
                    if (mode is "command-stall" or "cancel-callback-stall")
                        Check(cleanups == 0, "models remain alive until command work and cancellation callbacks finish");
                    Check(shell.Message.Contains("budget") && shell.Message.Contains("not confirmed"), "timeout diagnostic states the resource uncertainty");
                    _ = RFacade.Shutdown(4000); // must neither extend the budget nor start another attempt
                    release.TrySetResult();
                    await Wait(cleaned.Task);
                }
                await Wait(shell.Terminal.Task);
            }
        }
        await Task.Delay(130);
        Check(shell.States.Count(s => s is RApplicationState.Failed or RApplicationState.Stopped) == 1, "only one terminal notification, including after late completion");
        Check(shell.States.First() == RApplicationState.Initialising && shell.States.Contains(RApplicationState.ShuttingDown), "ordered lifecycle includes Initialising and ShuttingDown");
        bool fails = mode is "command-stall" or "cancel-callback-stall" or "cleanup-sync-stall" or "cleanup-async-stall" or "cleanup-throw" or "zero" or "constructor-stall" or "initialisation-stall" or "startup-cleanup-timeout";
        Check(shell.Terminal.Task.Result == (fails ? RApplicationState.Failed : RApplicationState.Stopped), "terminal state matches cleanup and deadline result");
        Check(!shell.States.Contains(RApplicationState.Running) || mode != "reentrant-initialising", "reentrant shutdown never resurrects Running");
        int before = cleanups;
        _ = RFacade.Shutdown(4000);
        await Task.Delay(30);
        Check(cleanups == before, "repeated Shutdown never repeats cleanup or retries a failed attempt");
        Check(Program.ThrowsLookup(() => Root.Instance.Send(new Extra()), "shutdown"), "ordinary submissions are rejected after admission closes");
        if (mode == "queue") Check(secondRan == 0, "unstarted sequential batch members do not execute");
        if (!fails && mode != "constructor-stall")
            Check(cleanupOrder.ToArray().SequenceEqual(new[] { "child", "root" }), "child resources clean up before parent resources");
    }

    public sealed class Root : RFacade
    {
        internal static Root Instance = null!;
        internal static int Constructors;
        private static readonly Sender Dispatcher = new();
        public Root()
        {
            Constructors++;
            Instance = this;
            if (mode == "constructor-stall") { started.TrySetResult(); release.Task.GetAwaiter().GetResult(); }
        }
        internal void Send(RCommandBase command) => Execute(command);
        internal void SubmitWork()
        {
            if (mode == "queue") Dispatcher.Batch();
            else Execute(new Work());
        }
        protected override IRModel[] RegisterModels() => [Dispatcher, new Resource("root")];
        protected override RMediator[] RegisterMediators() => [];
        protected override RCommandBase CreateInitialiseCommand(IRInitialisation? payload) => new Init();
    }
    public sealed class Child : RFacade
    {
        protected override IRModel[] RegisterModels() => [new Resource("child")];
        protected override RMediator[] RegisterMediators() => [];
        protected override RCommandBase CreateInitialiseCommand(IRInitialisation? payload) => new ChildInit();
    }
    private sealed class Sender : RModel
    {
        internal void Batch() => Execute(new Work(), new Extra());
    }
    private sealed class Init : RCommand<RVoid>
    {
        protected override async Task<RVoid> Run()
        {
            if (mode == "initialisation-stall") { started.TrySetResult(); await release.Task; }
            if (mode is "startup-failure" or "startup-cleanup-timeout") throw new IOException("original startup fault");
            InitialiseFacadeChild<Child>();
            return await Complete();
        }
    }
    private sealed class ChildInit : RCommand<RVoid>
    {
        protected override Task<RVoid> Run() => Complete();
    }
    private sealed class Stopper : RCommand<RVoid>
    {
        protected override Task<RVoid> Run() { _ = RFacade.Shutdown(3000); return Complete(); }
    }
    private sealed class Extra : RCommand<RVoid>
    {
        protected override Task<RVoid> Run() { Interlocked.Increment(ref secondRan); return Complete(); }
    }
    private sealed class Work : RCommand<RVoid>
    {
        protected override async Task<RVoid> Run()
        {
            var tracker = typeof(RCommandBase).GetProperty("Tracker", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(this)!;
            var token = (CancellationToken)tracker.GetType().GetProperty("Token", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tracker)!;
            using var registration = token.Register(() =>
            {
                if (mode == "cancel-callback-stall") release.Task.GetAwaiter().GetResult();
                if (mode is "active" or "queue" or "cancel-callback-stall") release.TrySetResult();
            });
            started.TrySetResult();
            await release.Task;
            return await Complete();
        }
    }
    private sealed class Resource(string name) : RModel
    {
        public override async Task Cleanup()
        {
            Interlocked.Increment(ref cleanups);
            cleanupOrder.Enqueue(name);
            if (name == "child" && mode == "cleanup-sync-stall") release.Task.GetAwaiter().GetResult();
            if (name == "child" && mode == "cleanup-async-stall") await release.Task;
            if (name == "child" && mode == "startup-cleanup-timeout") { started.TrySetResult(); await release.Task; }
            if (name == "root") cleaned.TrySetResult();
            if (name == "child" && mode == "cleanup-throw") throw new IOException("child cleanup fault");
        }
    }
    private sealed class HostShell : IRShell
    {
        internal readonly ConcurrentQueue<RApplicationState> States = new();
        internal readonly TaskCompletionSource Ready = Signal();
        internal readonly TaskCompletionSource<RApplicationState> Terminal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal string Message = "";
        public void HandleStateChanged(RApplicationState state, string? message = null)
        {
            States.Enqueue(state);
            if (state == RApplicationState.Running) Ready.TrySetResult();
            if (state is RApplicationState.Stopped or RApplicationState.Failed) { Message = message ?? ""; Terminal.TrySetResult(state); }
            if (state == RApplicationState.Initialising && mode == "reentrant-initialising"
                || state == RApplicationState.Running && mode == "reentrant-running") _ = RFacade.Shutdown(3000);
            if (mode == "throwing-callback") throw new Exception("host notification fault");
        }
        public bool ShowMessageBox(string title, string message, bool isYesNo = false) => true;
    }
}
