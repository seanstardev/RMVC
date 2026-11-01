using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RMVC
{
    /// <summary>Owns active root registration, cancellation waits and ordered operation notifications.</summary>
    internal sealed class ROperationRegistry
    {
        private readonly object operationGate = new object();
        private readonly Dictionary<CancellationTokenSource, RTracker> activeOperations =
            new Dictionary<CancellationTokenSource, RTracker>();
        private readonly Queue<bool> operationNotifications = new Queue<bool>();
        private bool deliveringOperationNotifications;

        private TaskCompletionSource<bool>? abortWait;

        private Timer? abortWaitTimer;

        private int outstandingCancellationPasses;

        private readonly Action<bool> notifyOperationState;

        internal ROperationRegistry(Action<bool> notifyOperationState)
        {
            this.notifyOperationState = notifyOperationState;
        }

        internal void AbortCommands(Type facadeType)
        {
            CancellationTokenSource[] operations;

            lock (operationGate)
            {
                var matching = new List<CancellationTokenSource>();

                foreach (var operation in activeOperations)
                {
                    if (operation.Value.Facade.GetType() == facadeType)
                        matching.Add(operation.Key);
                }

                operations = matching.ToArray();
            }

            CancelOperations(operations);
        }

        internal void AbortAllCommands(RTracker? excluded = null)
        {
            CancellationTokenSource[] operations;

            lock (operationGate)
            {
                var matching = new List<CancellationTokenSource>();

                foreach (var operation in activeOperations)
                {
                    if (excluded == null
                        || operation.Value.CommandRootId != excluded.CommandRootId)
                    {
                        matching.Add(operation.Key);
                    }
                }

                operations = matching.ToArray();
            }

            CancelOperations(operations);
        }

        internal Task<bool> AbortAllOtherCommands(
            RTracker caller,
            uint? timeoutMilliseconds)
        {
            if (timeoutMilliseconds == uint.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timeoutMilliseconds),
                    "Use null for an unlimited wait.");
            }

            lock (operationGate)
            {
                if (abortWait != null)
                {
                    RFacade.Log("ABORT WAIT IGNORED: another request is already active.");

                    return Task.FromResult(false);
                }

                if (activeOperations.Count == 1 && outstandingCancellationPasses == 0)
                    return Task.FromResult(true);

                var completion = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                if (timeoutMilliseconds.HasValue)
                {
                    abortWaitTimer = new Timer(
                        _ =>
                        {
                            lock (operationGate)
                            {
                                if (ReferenceEquals(abortWait, completion))
                                    CompleteAbortWait(false);
                            }
                        },
                        null,
                        timeoutMilliseconds.Value,
                        uint.MaxValue);
                }

                abortWait = completion;
                outstandingCancellationPasses++;

                _ = Task.Run(() =>
                {
                    try
                    {
                        AbortAllCommands(caller);
                    }
                    finally
                    {
                        lock (operationGate)
                        {
                            // Cancellation can outlive its wait's timeout.
                            outstandingCancellationPasses--;

                            TryCompleteAbortWait();
                        }
                    }
                });

                return completion.Task;
            }
        }

        // Called while operationGate is held.
        private void TryCompleteAbortWait()
        {
            if (abortWait != null
                && outstandingCancellationPasses == 0
                && activeOperations.Count == 1)
            {
                CompleteAbortWait(true);
            }
        }

        // Called while operationGate is held.
        private void CompleteAbortWait(bool completed)
        {
            var completion = abortWait;

            abortWait = null;

            abortWaitTimer?.Dispose();
            abortWaitTimer = null;

            completion?.TrySetResult(completed);
        }

        private void CancelOperations(CancellationTokenSource[] operations)
        {
            foreach (var source in operations)
            {
                try 
                { 
                    source.Cancel(); 
                }
                catch (ObjectDisposedException)
                {
                    // The operation finished after the Abort snapshot was taken.
                }
                catch (AggregateException ex)
                {
                    // A throwing cancellation callback must not prevent other workers being cancelled.
                    RFacade.Log($"CANCELLATION CALLBACK ERROR: {ex}");
                }
            }
        }

        internal bool TryRegister(CancellationTokenSource source, RTracker tracker)
        {
            var commandType = tracker.Command.GetType();

            // Evaluate application overrides outside the framework lock.
            var allowConcurrentRoots = tracker.Command.AllowConcurrentRoots;
            int activeCount;
            
            lock (operationGate)
            {
                if (!allowConcurrentRoots)
                {
                    foreach (var active in activeOperations.Values)
                    {
                        if (ReferenceEquals(active.Facade, tracker.Facade) &&
                            active.Command.GetType() == commandType)
                            return false;
                    }
                }

                activeOperations.Add(source, tracker);
                activeCount = activeOperations.Count;

                if (activeOperations.Count == 1)
                    operationNotifications.Enqueue(true);

            }
            if (activeCount > 1)
                RFacade.Log($"ROOTS CONCURRENT: activeCount={activeCount}.");

            DeliverOperationNotifications();

            return true;
        }

        internal void Unregister(CancellationTokenSource source)
        {
            lock (operationGate)
            {
                if (activeOperations.Remove(source) && activeOperations.Count == 0)
                    operationNotifications.Enqueue(false);

                TryCompleteAbortWait();
            }

            DeliverOperationNotifications();
        }

        private void DeliverOperationNotifications()
        {
            lock (operationGate)
            {
                if (deliveringOperationNotifications || operationNotifications.Count == 0)
                    return;

                deliveringOperationNotifications = true;
            }

            while (true)
            {
                bool started;

                lock (operationGate)
                {
                    if (operationNotifications.Count == 0)
                    {
                        deliveringOperationNotifications = false;
                        return;
                    }
                    started = operationNotifications.Dequeue();
                }

                // Serialise notifications, but never call application code under the tracking lock.
                try
                {
                    notifyOperationState(started);
                }
                catch (Exception ex)
                {
                    RFacade.Log($"OPERATION NOTIFICATION ERROR: started={started}; {ex}");
                }
            }
        }

    }
}
