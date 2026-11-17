using System;
using System.Threading.Tasks;

namespace RMVC
{
    /// <summary>Guards sequential children and drains outstanding work before ancestor reservations can leave.</summary>
    internal sealed class RCommandChildExecution
    {
        internal TaskCompletionSource<bool> Begin(RCommandBase parent)
        {
            lock (gate)
            {
                if (exiting || pending != null)
                    throw new InvalidOperationException(
                        "Await children sequentially; overlapping or exiting child execution is invalid: "
                            + parent.Name + ".");

                pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                return pending;
            }
        }

        internal void Finish(TaskCompletionSource<bool> completion)
        {
            lock (gate)
            {
                pending = null;
                completion.TrySetResult(true);
            }
        }

        internal async Task DrainBeforeExit(RTracker tracker)
        {
            Task? unfinished;

            lock (gate)
            {
                exiting = true;
                unfinished = pending?.Task;
            }

            if (unfinished != null)
            {
                const string message = "Command exited while a child was still running. Await every child Execute call.";

                tracker.SetError(message);
                RCommandLockCoordinator.Report("COMMAND CHILD NOT AWAITED: command=" + tracker.Command.Name
                    + "; commandId=" + tracker.CommandId + "; root=" + tracker.CommandRootId);

                // Do not release ancestor reservations while that child still uses them.
                await unfinished.ConfigureAwait(false);
            }
        }

        private readonly object gate = new object();

        private TaskCompletionSource<bool>? pending;

        private bool exiting;
    }
}
