using System.Collections;
using System.Reflection;
using RMVC;

namespace RMVC.ContractLookupChecks
{
    internal static class AbortWaitChecks
    {
        private static TaskCompletionSource<bool> Signal()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);

        private static int ActiveCount()
        {
            const BindingFlags staticFlags = BindingFlags.Static | BindingFlags.NonPublic;
            const BindingFlags instanceFlags = BindingFlags.Instance | BindingFlags.NonPublic;
            object registry = typeof(RFacade).GetField("operations", staticFlags)!.GetValue(null)!;
            object gate = registry.GetType().GetField("operationGate", instanceFlags)!.GetValue(registry)!;
            var operations = (IDictionary)registry.GetType()
                .GetField("activeOperations", instanceFlags)!.GetValue(registry)!;

            lock (gate)
                return operations.Count;
        }

        private static async Task WaitForCount(int count)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);

            while (ActiveCount() != count && DateTime.UtcNow < deadline)
                await Task.Delay(10);

            Program.Check(ActiveCount() == count,
                "operation registry reaches " + count + " active roots");
        }

        private static async Task<T> Await<T>(Task<T> task)
            => await task.WaitAsync(TimeSpan.FromSeconds(5));

        public static async Task Run(bool checkBlockedCallbackRetry = false)
        {
            RFacade.Create<HostFacade>(new CheckShell(), RFacade.Child<OtherFacade>());

            await Await(HostFacade.Ready.Task);
            await WaitForCount(0);

            var alone = new WaitingCommand();
            HostFacade.Dispatcher.Dispatch(alone);

            Program.Check(await Await(alone.Result.Task) && !alone.WasAborted,
                "no other roots returns true without cancelling the caller");

            await WaitForCount(0);

            // The cancellation pass returns first; the command must still finish.
            var delayedRoot = new Worker(stopOnCancel: false);
            OtherFacade.Dispatcher.Dispatch(delayedRoot);
            await Await(delayedRoot.Started.Task);

            var first = new WaitingCommand();
            HostFacade.Dispatcher.Dispatch(first);
            await Await(delayedRoot.Cancelled.Task);
            await Task.Delay(40);

            Program.Check(!first.Result.Task.IsCompleted,
                "a returned cancellation pass does not bypass an unfinished root");

            delayedRoot.Stop.TrySetResult(true);

            Program.Check(await Await(first.Result.Task) && !first.WasAborted,
                "other-facade cancellation finishes without aborting the caller");

            await WaitForCount(0);

            // The root returns first; its cancellation callback is still running.
            var callbackRelease = Signal();
            var delayedCallback = new Worker(true, callbackRelease.Task);
            OtherFacade.Dispatcher.Dispatch(delayedCallback);
            await Await(delayedCallback.Started.Task);

            var second = new WaitingCommand();
            HostFacade.Dispatcher.Dispatch(second);
            await Await(delayedCallback.Cancelled.Task);
            await WaitForCount(1);

            Program.Check(!second.Result.Task.IsCompleted,
                "the final root finishing does not bypass its running cancellation callback");

            callbackRelease.TrySetResult(true);

            Program.Check(await Await(second.Result.Task)
                && delayedCallback.CallbackFinished.Task.IsCompleted,
                "success waits for both command roots and cancellation callbacks");

            await WaitForCount(0);

            var timeoutRelease = Signal();
            var blockedCallback = new Worker(true, timeoutRelease.Task);
            OtherFacade.Dispatcher.Dispatch(blockedCallback);
            await Await(blockedCallback.Started.Task);

            var callbackTimeout = new WaitingCommand(50);
            HostFacade.Dispatcher.Dispatch(callbackTimeout);
            await Await(blockedCallback.Cancelled.Task);

            Program.Check(!await Await(callbackTimeout.Result.Task)
                && !blockedCallback.CallbackFinished.Task.IsCompleted,
                "timeout remains observable while a cancellation callback is blocked");

            if (checkBlockedCallbackRetry)
            {
                await WaitForCount(0);

                var callbackRetry = new WaitingCommand(100);
                HostFacade.Dispatcher.Dispatch(callbackRetry);

                bool reportedFinished = await Await(callbackRetry.Result.Task);
                bool callbackStillRunning = !blockedCallback.CallbackFinished.Task.IsCompleted;

                timeoutRelease.TrySetResult(true);
                await Await(blockedCallback.CallbackFinished.Task);
                await WaitForCount(0);

                Program.Check(!(reportedFinished && callbackStillRunning),
                    "retry must not report success while an earlier cancellation pass is still running");
            }

            timeoutRelease.TrySetResult(true);
            await Await(blockedCallback.CallbackFinished.Task);
            await WaitForCount(0);

            var stubborn = new Worker(stopOnCancel: false);
            OtherFacade.Dispatcher.Dispatch(stubborn);
            await Await(stubborn.Started.Task);

            var timed = new WaitingCommand(50);
            HostFacade.Dispatcher.Dispatch(timed);

            Program.Check(!await Await(timed.Result.Task) && !timed.WasAborted,
                "timeout returns false while unfinished work remains and the caller stays uncancelled");

            await WaitForCount(1);

            var retry = new WaitingCommand(null);
            HostFacade.Dispatcher.Dispatch(retry);
            await Await(retry.Requested.Task);

            Program.Check(!await Await(retry.Request(1500))
                && !retry.Result.Task.IsCompleted,
                "an overlapping request returns false without replacing the original wait");

            stubborn.Stop.TrySetResult(true);

            Program.Check(await Await(retry.Result.Task),
                "an unlimited request can succeed after an earlier request timed out");

            await WaitForCount(0);

            var childTarget = new Worker(stopOnCancel: true);
            OtherFacade.Dispatcher.Dispatch(childTarget);
            await Await(childTarget.Started.Task);

            var nested = new WaitingCommand();
            var parent = new ParentCommand(nested);
            HostFacade.Dispatcher.Dispatch(parent);

            Program.Check(await Await(nested.Result.Task)
                && await Await(parent.Result.Task)
                && !nested.WasAborted,
                "a child excludes its whole root chain from cancellation");

            await WaitForCount(0);

            var originalTarget = new Worker(stopOnCancel: true);
            OtherFacade.Dispatcher.Dispatch(originalTarget);
            await Await(originalTarget.Started.Task);

            var original = new OriginalAbortCommand();
            HostFacade.Dispatcher.Dispatch(original);

            Program.Check(await Await(original.Result.Task),
                "existing AbortAllCommands still cancels its own caller");

            await Await(originalTarget.Cancelled.Task);
            await WaitForCount(0);
        }

        public sealed class Dispatcher : RModel
        {
            public void Dispatch(RCommandBase command) => base.Execute(command);
        }

        public sealed class HostFacade : RFacade
        {
            internal static readonly Dispatcher Dispatcher = new();
            internal static readonly TaskCompletionSource<bool> Ready = Signal();

            protected override IRModel[] RegisterModels() => new IRModel[] { Dispatcher };
            protected override RMediator[] RegisterMediators() => Array.Empty<RMediator>();
            protected override RCommandBase CreateInitialiseCommand(IRInitialisation? initialisation)
                => new HostInitialiseCommand();
        }

        public sealed class OtherFacade : RFacade
        {
            internal static readonly Dispatcher Dispatcher = new();

            protected override IRModel[] RegisterModels() => new IRModel[] { Dispatcher };
            protected override RMediator[] RegisterMediators() => Array.Empty<RMediator>();
            protected override RCommandBase CreateInitialiseCommand(IRInitialisation? initialisation)
                => new EmptyCommand();
        }

        private sealed class HostInitialiseCommand : RCommand<RVoid>
        {
            protected override async Task<RVoid> Run()
            {
                base.InitialiseFacadeChild<OtherFacade>();
                HostFacade.Ready.TrySetResult(true);

                return await Complete();
            }
        }

        private sealed class EmptyCommand : RCommand<RVoid>
        {
            protected override Task<RVoid> Run() => Complete();
        }

        private sealed class Worker : RCommand<RVoid>
        {
            private readonly bool stopOnCancel;
            private readonly Task? callbackRelease;

            internal readonly TaskCompletionSource<bool> Started = Signal();
            internal readonly TaskCompletionSource<bool> Cancelled = Signal();
            internal readonly TaskCompletionSource<bool> CallbackFinished = Signal();
            internal readonly TaskCompletionSource<bool> Stop = Signal();

            internal Worker(bool stopOnCancel, Task? callbackRelease = null)
            {
                this.stopOnCancel = stopOnCancel;
                this.callbackRelease = callbackRelease;
            }

            protected override async Task<RVoid> Run()
            {
                // Reflection is confined to the fixture: commands do not publicly
                // expose their framework cancellation token.
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                object tracker = typeof(RCommandBase)
                    .GetProperty("Tracker", flags)!.GetValue(this)!;
                var token = (CancellationToken)tracker.GetType()
                    .GetProperty("Token", flags)!.GetValue(tracker)!;

                token.Register(() =>
                {
                    Cancelled.TrySetResult(true);

                    if (stopOnCancel)
                        Stop.TrySetResult(true);

                    callbackRelease?.GetAwaiter().GetResult();
                    CallbackFinished.TrySetResult(true);
                });

                Started.TrySetResult(true);
                await Stop.Task;

                return await Complete();
            }
        }

        private sealed class WaitingCommand : RCommand<bool>
        {
            private readonly uint? timeout;

            internal readonly TaskCompletionSource<bool> Requested = Signal();
            internal readonly TaskCompletionSource<bool> Result = Signal();
            internal bool WasAborted;

            internal WaitingCommand(uint? timeout = 1500)
            {
                this.timeout = timeout;
            }

            internal Task<bool> Request(uint? milliseconds)
                => base.AbortAllOtherCommands(milliseconds);

            protected override async Task<bool> Run()
            {
                try
                {
                    Task<bool> wait = Request(timeout);
                    Requested.TrySetResult(true);

                    bool completed = await wait;
                    WasAborted = base.AbortRequested;
                    Result.TrySetResult(completed);

                    return completed;
                }
                catch (Exception error)
                {
                    Result.TrySetException(error);

                    throw;
                }
            }
        }

        private sealed class ParentCommand : RCommand<bool>
        {
            private readonly WaitingCommand child;
            internal readonly TaskCompletionSource<bool> Result = Signal();

            internal ParentCommand(WaitingCommand child)
            {
                this.child = child;
            }

            protected override async Task<bool> Run()
            {
                bool completed = await base.Execute(child);
                Result.TrySetResult(completed && !base.AbortRequested);

                return completed;
            }
        }

        private sealed class OriginalAbortCommand : RCommand<bool>
        {
            internal readonly TaskCompletionSource<bool> Result = Signal();

            protected override Task<bool> Run()
            {
                base.AbortAllCommands();
                Result.TrySetResult(base.AbortRequested);

                return Complete(base.AbortRequested);
            }
        }

        private sealed class CheckShell : IRShell
        {

            public void HandleStateChanged(RMVC.RApplicationState state, string? detail = null)
        {
            if (state != RMVC.RApplicationState.Failed) return;
            string message = detail ?? "";
            string facadeName = message.StartsWith("Facade: ") ? message.Split('\n')[0][8..].Trim() : "";
            if (message.StartsWith("Facade: ")) message = message[(message.IndexOf('\n') + 1)..];
            HostFacade.Ready.TrySetException(new Exception(message));
        }

            public bool ShowMessageBox(string title, string message, bool isYesNo = false)
                => false;
        }
    }
}
