using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace RMVC
{
    /// <summary>Owns the one application's lifecycle, ordered host notifications and shutdown deadline.</summary>
    internal sealed class RApplicationLifecycle
    {
        private readonly object gate = new object();
        private readonly ROperationRegistry operations;
        private readonly Queue<KeyValuePair<RApplicationState, string?>> notifications 
            = new Queue<KeyValuePair<RApplicationState, string?>>();

        private IRShell? shell;
        private RApplicationState? state;
        private readonly TaskCompletionSource<bool> shutdownCompletion =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool delivering;
        private bool shutdownRequested;
        private bool accepting = true;
        private Timer? deadlineTimer;
        private long shutdownStarted;
        private uint shutdownBudget;
        private string shutdownStage = "shutdown";

        internal RApplicationLifecycle(ROperationRegistry operations) 
        { 
            this.operations = operations;
            // Shell-only callers may discard the Task; faults still remain observable to awaiters.
            _ = shutdownCompletion.Task.ContinueWith(task => { _ = task.Exception; },
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default); 
        }

        internal IRShell Shell {
            get 
            { 
                lock (gate) return shell ?? 
                        throw new InvalidOperationException(
                            "The application Shell is unavailable before accepted Create."); 
            } 
        }

        internal bool ShutdownRequested { get { lock (gate) return shutdownRequested; } }
        internal bool AcceptingCommands { get { lock (gate) return accepting; } }

        internal void Begin(IRShell host)
        {
            lock (gate) { shell = host; SetState(RApplicationState.Initialising, null); }
            Deliver();
        }

        internal bool Running()
        {
            lock (gate)
            {
                if (shutdownRequested || state != RApplicationState.Initialising || !accepting) return false;
                SetState(RApplicationState.Running, null);
            }
            Deliver();
            return AcceptingCommands;
        }

        internal void CloseAdmission()
        {
            lock (gate) accepting = false;
            operations.CloseAdmission();
        }

        internal void StartupFailed(string message)
        {
            lock (gate)
            {
                if (shutdownRequested) return;
                SetState(RApplicationState.Failed, message);
                shutdownCompletion.TrySetException(new InvalidOperationException(message));
            }
            Deliver();
        }

        internal Task Shutdown(uint timeoutMilliseconds, Func<Task<IReadOnlyList<Exception>>> work, RTracker? coordinator = null)
        {
            lock (gate)
            {
                if (shell == null) throw new InvalidOperationException("Shutdown requires an accepted Create call.");
                if (shutdownRequested || state == RApplicationState.Stopped || state == RApplicationState.Failed)
                {
                    if (coordinator != null && !coordinator.IsShutdownCoordinator)
                        throw new InvalidOperationException("Shutdown was already initiated elsewhere; an active command cannot join its own drain. Finish this command instead.");
                    return shutdownCompletion.Task;
                }
                if (coordinator != null && state != RApplicationState.Running)
                    throw new InvalidOperationException("A command can await Shutdown only after the entire composition has completed initialisation.");
                operations.CloseAdmission(coordinator);
                shutdownRequested = true;
                accepting = false;
                shutdownStarted = Stopwatch.GetTimestamp();
                shutdownBudget = timeoutMilliseconds;
                SetState(RApplicationState.ShuttingDown, null);
                deadlineTimer = new Timer(_ => Finish(null, true), null,
                    Timeout.Infinite, Timeout.Infinite);
                // uint.MaxValue is reserved by Timer; the public boundary rejects it.
                deadlineTimer.Change(timeoutMilliseconds, uint.MaxValue);
            }
            // Begin work independently: a synchronous cancellation callback or Cleanup
            // implementation must not prevent the deadline from being observed.
            _ = Task.Run(async () =>
            {
                try
                {
                    var errors = await work().ConfigureAwait(false);
                    Finish(errors, false);
                }
                catch (Exception error) { Finish(new[] { error }, false); }
            });
            _ = Task.Run(Deliver);
            return shutdownCompletion.Task;
        }
        internal void SetShutdownStage(string value) { lock (gate) shutdownStage = value; }
        private void Finish(IReadOnlyList<Exception>? errors, bool timeout)
        {
            lock (gate)
            {
                if (state != RApplicationState.ShuttingDown) return;
                double elapsed = (Stopwatch.GetTimestamp() - shutdownStarted) * 1000d / Stopwatch.Frequency;
                timeout |= elapsed >= shutdownBudget;
                if (timeout)
                {
                    string message = $"RMVC shutdown exceeded its {shutdownBudget} ms budget while {shutdownStage}. " +
                        "Commands or cleanup may still be running; resources are not confirmed released.";
                    SetState(RApplicationState.Failed, message);
                    shutdownCompletion.TrySetException(new TimeoutException(message));
                }
                else if (errors != null && errors.Count > 0)
                {
                    string message = "RMVC shutdown cleanup failed:";
                    foreach (var error in errors) message += Environment.NewLine + error.Message;
                    SetState(RApplicationState.Failed, message);
                    shutdownCompletion.TrySetException(new AggregateException(message, errors));
                }
                else
                {
                    SetState(RApplicationState.Stopped, null);
                    shutdownCompletion.TrySetResult(true);
                }
                deadlineTimer?.Dispose();
                deadlineTimer = null;
            }
            Deliver();
        }
        // Called under gate; host code is only invoked by Deliver, outside the gate.
        private void SetState(RApplicationState value, string? message)
        {
            state = value;
            notifications.Enqueue(new KeyValuePair<RApplicationState, string?>(value, message));
        }
        private void Deliver()
        {
            lock (gate)
            {
                if (delivering) return;
                delivering = true;
            }
            while (true)
            {
                KeyValuePair<RApplicationState, string?> notification;
                IRShell host;
                lock (gate)
                {
                    if (notifications.Count == 0) { delivering = false; return; }
                    notification = notifications.Dequeue();
                    host = shell!;
                }
                try { host.HandleStateChanged(notification.Key, notification.Value); }
                catch (Exception error) { RFacade.Log($"LIFECYCLE NOTIFICATION ERROR: state={notification.Key}; {error}"); }
            }
        }
    }
}
