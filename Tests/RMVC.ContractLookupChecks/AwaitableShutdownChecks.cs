using RMVC;
namespace RMVC.ContractLookupChecks;

internal static class AwaitableShutdownChecks
{
    static string mode = "";
    static readonly TaskCompletionSource finished = Signal();
    static readonly TaskCompletionSource entered = Signal();
    static readonly TaskCompletionSource release = Signal();
    static readonly TaskCompletionSource cleaned = Signal();
    static int cleanups, progressAfterCleanup, exitHooksBlocked;
    static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    static void Check(bool value, string text) => Program.Check(value, mode + ": " + text);
    static Task Wait(Task task) => task.WaitAsync(TimeSpan.FromSeconds(8));

    internal static async Task Run(string scenario)
    {
        mode = scenario;
        if (mode == "unbound")
        {
            new Coordinator().CheckUnbound();
            return;
        }
        var shell = new HostShell();
        RFacade.Create<Host>(shell);
        await Wait(shell.Ready.Task);
        await TestRuntime.WaitIdle();
        if (mode == "initialising")
        {
            Check(Host.InitRejected && cleanups == 0, "awaited shutdown in initialisation is rejected without starting shutdown");
            await RFacade.Shutdown(3000);
        }
        else if (mode == "invalid-budget")
        {
            Host.Instance.Send(new InvalidBudget());
            await Wait(finished.Task);
            await TestRuntime.WaitIdle();
            await RFacade.Shutdown(3000);
        }
        else if (mode == "blocked-prompt")
        {
            Host.Instance.Send(new PromptWorker());
            await Wait(entered.Task);
            var task = RFacade.Shutdown(60);
            try { await Wait(task); Check(false, "timeout required"); }
            catch (TimeoutException) { Check(true, "shutdown Task faults while a synchronous Shell prompt still holds a command"); }
            Check(cleanups == 0, "timeout does not clean actors still used by the prompt-blocked command");
            release.TrySetResult();
            await Wait(cleaned.Task);
            Check(shell.States.Count(x => x == RApplicationState.Failed) == 1
                && !shell.States.Contains(RApplicationState.Stopped), "late prompt completion cannot change terminal Failed to Stopped");
        }
        else if (mode is "external" or "external-fail" or "blocked-notification")
        {
            var first = RFacade.Shutdown(3000);
            var second = RFacade.Shutdown(5000);
            Check(ReferenceEquals(first, second), "external callers share one Task and one shutdown attempt");
            try { await Wait(first); Check(mode != "external-fail", "external caller can await successful shutdown"); }
            catch (AggregateException error) { Check(mode == "external-fail" && error.InnerExceptions.Any(x => x.GetBaseException().Message == "fixture cleanup"), "cleanup exception is retained by the awaitable failure"); }
            if (mode == "blocked-notification") release.TrySetResult();
            Check(cleanups == 1, "external shutdown cleans once");
        }
        else
        {
            if (mode is "timeout" or "join-other")
            {
                Host.Instance.Send(new StalledWorker());
                await Wait(entered.Task);
            }
            if (mode == "join-other")
            {
                var external = RFacade.Shutdown(3000);
                release.TrySetResult();
                await Wait(external);
                await Wait(finished.Task);
            }
            else
            {
                Host.Instance.Send(mode == "nested" ? new Parent() : new Coordinator());
                await Wait(finished.Task);
            }
            if (mode == "timeout")
            {
                Check(cleanups == 0, "coordinator timeout does not race a stalled root with cleanup");
                release.TrySetResult();
                await Wait(cleaned.Task);
            }
            await TestRuntime.WaitIdle();
            Check(progressAfterCleanup == 0, "coordinator completion does not dispatch progress commands after cleanup");
            if (mode != "join-other")
                Check(exitHooksBlocked == (mode == "nested" ? 2 : 1), "command exit hooks inherit the root-wide actor restriction");
        }
        await Wait(shell.Terminal.Task);
        Check(cleanups == 1, "model cleanup is attempted exactly once");
    }

    public sealed class Resource : RModel
    {
        public override async Task Cleanup()
        {
            Interlocked.Increment(ref cleanups);
            try
            {
                if (mode is "cleanup-fail" or "external-fail") throw new InvalidOperationException("fixture cleanup");
                await Task.Yield();
            }
            finally { cleaned.TrySetResult(); }
        }
    }
    public sealed class Host : RFacade
    {
        internal static Host Instance = null!;
        internal static bool InitRejected;
        public Host() { Instance = this; }
        public void Send(RCommandBase command) => Execute(command);
        protected override IRModel[] RegisterModels() => [new Resource()];
        protected override RMediator[] RegisterMediators() => [];
        protected override RCommandBase CreateInitialiseCommand(IRInitialisation? data) => new Init();
        protected override RCommandBase? CreateProgressUpdateCommand(string id, RProgress[] items) => new Progress();
        protected override RCommandBase? CreateProgressCompleteCommand(string id) => new Progress();
    }
    sealed class Init : RCommand<RVoid>
    {
        protected override Task<RVoid> Run()
        {
            if (mode == "initialising")
                Host.InitRejected = Program.ThrowsLookup(() => Shutdown(3000), "initialisation");
            return Complete();
        }
    }
    sealed class Progress : RCommand<RVoid>
    {
        protected override Task<RVoid> Run()
        {
            if (mode == "progress-caller")
                Check(Program.ThrowsLookup(() => Shutdown(3000), "active ordinary command root"), "progress callback cannot become a shutdown coordinator");
            if (cleanups != 0) Interlocked.Increment(ref progressAfterCleanup);
            _ = Model<Resource>();
            return Complete();
        }
    }
    sealed class Parent : RCommand<RVoid>
    {
        protected override void OnCommandExit(bool success)
        {
            if (Program.ThrowsLookup(() => Model<Resource>(), "shutdown-coordinating"))
                Interlocked.Increment(ref exitHooksBlocked);
        }

        protected override async Task<RVoid> Run()
        {
            try
            {
                await Execute(new Coordinator());
                Check(Program.ThrowsLookup(() => Model<Resource>(), "shutdown-coordinating"), "parent resuming after nested shutdown cannot resolve actors");
                Check(Program.ThrowsLookup(() => Execute((RCommandBase)new Progress()), "shutdown-coordinating"), "parent resuming after nested shutdown cannot execute children");
                Check(Shell.ShowMessageBox("after", "local decision"), "parent retains Shell access");
                finished.TrySetResult();
            }
            catch (Exception error) { finished.TrySetException(error); }
            return await Complete();
        }
    }
    sealed class InvalidBudget : RCommand<RVoid>
    {
        protected override Task<RVoid> Run()
        {
            try
            {
                bool rejected = false;
                try { _ = Shutdown(uint.MaxValue); } catch (ArgumentOutOfRangeException) { rejected = true; }
                Check(rejected && Model<Resource>() != null, "invalid budget does not restrict the root or begin shutdown");
                finished.TrySetResult();
            }
            catch (Exception error) { finished.TrySetException(error); }
            return Complete();
        }
    }
    sealed class Coordinator : RCommand<RVoid>
    {
        protected override void OnCommandExit(bool success)
        {
            if (Program.ThrowsLookup(() => Model<Resource>(), "shutdown-coordinating"))
                Interlocked.Increment(ref exitHooksBlocked);
        }

        internal void CheckUnbound() => Check(Program.ThrowsLookup(() => Shutdown(100), "running command"), "unbound command cannot initiate shutdown");
        protected override async Task<RVoid> Run()
        {
            try
            {
                await SetProgressTitle("Before shutdown");
                var task = Shutdown(mode == "timeout" ? 60u : 3000u);
                Check(ReferenceEquals(task, Shutdown(4000)), "repeated calls from the coordinator retain the original task and budget");
                VerifyRestricted();
                RFacade.AbortAllCommands();
                try
                {
                    await task;
                    Check(mode is not "timeout" and not "cleanup-fail", "coordinator awaits shutdown without waiting on itself");
                }
                catch (TimeoutException) { Check(mode == "timeout", "coordinator observes shutdown timeout as an exception"); }
                catch (AggregateException error) { Check(mode == "cleanup-fail" && error.InnerExceptions.Any(x => x.GetBaseException().Message == "fixture cleanup"), "coordinator observes cleanup failure with its original cause"); }
                Check(ReferenceEquals(task, Shutdown(4000)), "terminal calls retain the completed or faulted task without retry");
                Check(!AbortRequested, "shutdown preserves the coordinator's whole root from cancellation");
                VerifyRestricted();
                Check(Shell.ShowMessageBox("after", "local decision"), "Shell remains accessible after success or failure");
                await SetProgress(100);
                if (mode != "nested") finished.TrySetResult();
            }
            catch (Exception error) { finished.TrySetException(error); }
            return await Complete();
        }
        void VerifyRestricted()
        {
            Check(Program.ThrowsLookup(() => Model<Resource>(), "shutdown-coordinating")
                && Program.ThrowsLookup(() => ModelOrNull<Resource>(), "shutdown-coordinating")
                && Program.ThrowsLookup(() => ModelByContract<IDisposable>(), "shutdown-coordinating"), "required, optional and contract model lookups are blocked");
            Check(Program.ThrowsLookup(() => Mediator<ProbeMediator>(), "shutdown-coordinating")
                && Program.ThrowsLookup(() => MediatorOrNull<ProbeMediator>(), "shutdown-coordinating")
                && Program.ThrowsLookup(() => MediatorByContract<IDisposable>(), "shutdown-coordinating"), "required, optional and contract mediator lookups are blocked");
            Check(Program.ThrowsLookup(() => FacadeChild<Host>(), "shutdown-coordinating")
                && Program.ThrowsLookup(() => BridgeChild<ProbeBridge>(), "shutdown-coordinating")
                && Program.ThrowsLookup(() => FacadeParentByContract<IRFacade>(), "shutdown-coordinating"), "facade and bridge lookups are blocked");
            Check(Program.ThrowsLookup(() => Execute((RCommandBase)new Progress()), "shutdown-coordinating")
                && Program.ThrowsLookup(() => Execute<int>(new Value()), "shutdown-coordinating"), "typed and untyped child execution is blocked");
        }
    }
    public interface IProbe : IRContract { }
    sealed class ProbeMediator : RMediator<IProbe>
    {
        protected override void ActorAttached(IProbe actor) { }
        protected override void ActorDetached(IProbe actor) { }
    }
    sealed class ProbeBridge : RBridge<Host> { }
    sealed class Value : RCommand<int> { protected override Task<int> Run() => Complete(1); }
    sealed class StalledWorker : RCommand<RVoid>
    {
        protected override async Task<RVoid> Run()
        {
            entered.TrySetResult();
            await release.Task;
            if (mode == "join-other")
            {
                try { Check(Program.ThrowsLookup(() => Shutdown(3000), "already initiated"), "another active root cannot join a shutdown that must drain it"); finished.TrySetResult(); }
                catch (Exception error) { finished.TrySetException(error); }
            }
            return await Complete();
        }
    }
    sealed class PromptWorker : RCommand<RVoid>
    {
        protected override Task<RVoid> Run() { Shell.ShowMessageBox("blocked", "fixture prompt"); return Complete(); }
    }
    sealed class HostShell : IRShell
    {
        internal readonly TaskCompletionSource Ready = Signal();
        internal readonly TaskCompletionSource Terminal = Signal();
        internal readonly System.Collections.Concurrent.ConcurrentQueue<RApplicationState> States = new();
        public void HandleStateChanged(RApplicationState state, string? detail = null)
        {
            States.Enqueue(state);
            if (state == RApplicationState.Running) Ready.TrySetResult();
            if (state == RApplicationState.ShuttingDown && mode == "blocked-notification") release.Task.GetAwaiter().GetResult();
            if (state is RApplicationState.Stopped or RApplicationState.Failed) Terminal.TrySetResult();
        }
        public bool ShowMessageBox(string title, string text, bool isYesNo = false)
        {
            if (title == "blocked") { entered.TrySetResult(); release.Task.GetAwaiter().GetResult(); }
            return true;
        }
    }
}
