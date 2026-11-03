using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace RMVC
{
    internal class RTracker
    {
        internal string CommandId { get; }
        internal string CommandRootId => GetTrackerRoot().CommandId;
        private readonly object errorGate = new object();
        private readonly List<RErrorItem> errorItems = new List<RErrorItem>();

        internal bool Error
        {
            get
            {
                var root = GetTrackerRoot();

                lock (root.errorGate)
                    return root.errorItems.Count > 0;
            }
        }
        internal CancellationToken Token { get; }
        internal bool ErrorOrAbort { get { return Error || Token.IsCancellationRequested; } }
        internal RCommandBase Command { get; }
        internal RFacade Facade { get; }
        internal RTracker? TrackerParent { get; private set; }
        private int shutdownCoordinator;
        internal bool IsShutdownCoordinator => Volatile.Read(ref GetTrackerRoot().shutdownCoordinator) != 0;
        internal void RestrictToShutdown() => Interlocked.Exchange(ref GetTrackerRoot().shutdownCoordinator, 1);
        internal bool SameRoot(RTracker other) => ReferenceEquals(GetTrackerRoot(), other.GetTrackerRoot());
        internal bool ProgressRequested => GetTrackerRoot().progressRequested;
        private const int ProgressUpdateIntervalMilliseconds = 100;
        private readonly Stopwatch progressUpdateTimer = new Stopwatch();
        private readonly double weightInParent;
        private readonly bool reportsProgress;
        private bool progressRequested;

        internal void RequestProgress()
            => GetTrackerRoot().progressRequested = true;

        private RTracker? trackerChild;

        private double localPercent;
        private const double ProgressOverflowTolerance = 0.000001;
        private bool progressOverflowLogged;

        private string? title;
        private string? message;

        internal RTracker(
            RCommandBase command, 
            RFacade facade, 
            double weightInParent, 
            CancellationToken cancellationToken)
        {
            CommandId = Guid.NewGuid().ToString();
            this.weightInParent = weightInParent;
            Command = command;
            reportsProgress = command.ReportsProgress;

            Facade = facade;
            Token = cancellationToken;
        }

        internal RProgress[] GetProgressReport()
        {
            List<RProgress> list = new List<RProgress>();
            RTracker[] trackerPath = GetTrackersFlat();

            foreach (RTracker tracker in trackerPath)
            {
                if (!tracker.reportsProgress)
                    continue;

                list.Add(new RProgress(
                    Convert.ToInt32(Math.Round(tracker.localPercent)), // Final rounding here for report
                    tracker.message ?? string.Empty,
                    tracker.title ?? string.Empty,
                    tracker.CommandId
                ));
            }

            return list.ToArray();
        }

        internal async Task SetProgressTitle(string title)
        {
            this.title = title;
            await SendProgress();
        }
        internal async Task SetProgress(string text)
        {
            if (!string.IsNullOrWhiteSpace(text))
                message = text;

            await SendProgress();
        }

        internal async Task SetProgress(
            double percentComplete,
            string? text = null,
            bool forceReport = false)
        {
            percentComplete = RHelper.ClampPercent(percentComplete);

            if (!string.IsNullOrWhiteSpace(text) && text != message)
                message = text;
            
            if (percentComplete > localPercent)
            {
                Log($"PROGRESS SET: command={Command.GetType().FullName}; commandId={CommandId}; commandRootId={CommandRootId}; percent={percentComplete}; increment={percentComplete - localPercent}.");
                await UpdatePercent(percentComplete - localPercent, forceReport);
            }
            else
                await SendProgress(forceReport);
        }

        internal void SetError(string errorMessage, string? exceptionDetails = null)
        {
            if (string.IsNullOrWhiteSpace(errorMessage))
                errorMessage = "Unspecified Error.";

            var root = GetTrackerRoot();

            lock (root.errorGate)
            {
                root.errorItems.Insert(0, new RErrorItem(
                    errorMessage,
                    Command.GetType().FullName ?? Command.Name,
                    CommandId,
                    root.CommandId,
                    exceptionDetails));
            }
        }

        internal IReadOnlyList<RErrorItem> GetErrors()
        {
            var root = GetTrackerRoot();

            lock (root.errorGate)
                return Array.AsReadOnly(root.errorItems.ToArray());
        }

        internal RErrorItem? GetLatestError()
        {
            var root = GetTrackerRoot();

            lock (root.errorGate)
                return root.errorItems.Count == 0 ? null : root.errorItems[0];
        }

        internal RTracker CreateTrackerChild(RCommandBase command, double percentCap)
        {
            percentCap = RHelper.ClampPercent(percentCap);  // Ensure cap is within bounds
            
            Log($"CHILD ALLOCATED: command={command.GetType().FullName}; commandParentId={CommandId}; commandRootId={CommandRootId}; percentCap={percentCap}.");

            trackerChild = new RTracker(command, Facade, percentCap, Token);
            trackerChild.TrackerParent = this;

            return trackerChild;
        }

        private RTracker[] GetTrackersFlat()
        {
            List<RTracker> list = new List<RTracker>();

            RTracker trackerRoot = GetTrackerRoot();
            list.Add(trackerRoot);

            RTracker? trackerChild = trackerRoot.trackerChild;

            while (trackerChild != null)
            {
                list.Add(trackerChild);
                trackerChild = trackerChild.trackerChild;
            }

            return list.ToArray();
        }

        private async Task SendProgress(bool forceReport = false)
        {
            if (IsShutdownCoordinator || !GetTrackerRoot().progressRequested)
                return;

            if (TrackerParent != null)
            {
                await TrackerParent.SendProgress(forceReport);
                return;
            }

            // Only the root reaches here, so throttling is per chain.
            if (!forceReport &&
                progressUpdateTimer.IsRunning &&
                progressUpdateTimer.ElapsedMilliseconds < ProgressUpdateIntervalMilliseconds)
                return;

            progressUpdateTimer.Restart();

            var progressSet = GetProgressReport();

            if (progressSet.Length > 0)
                await Facade.HandleProgressChange(CommandId, progressSet).ConfigureAwait(false);
        }

        private async Task UpdatePercent(double localPercentIncrement, bool forceReport = false)
        {
            // Accumulate local percent without rounding for internal accuracy
            localPercent += localPercentIncrement;

            if (!progressOverflowLogged &&
                localPercent > 100d + ProgressOverflowTolerance)
            {
                progressOverflowLogged = true;

                Log(
                    $"PROGRESS OVERFLOW: command={Command.GetType().FullName}; " +
                    $"commandId={CommandId}; commandRootId={CommandRootId}; accumulated={localPercent}%. " +
                    "Check child command percentage allocations and direct SetProgress() calls.");
            }

            Log($"PROGRESS ACCUMULATED: command={Command.GetType().FullName}; commandId={CommandId}; commandRootId={CommandRootId}; percent={localPercent}.");

            if (TrackerParent != null)
            {
                // Calculate parent increment proportionally based on this tracker's cap and apply it immediately
                double parentIncrement = localPercentIncrement * (weightInParent / 100d);

                // Propagate to parent with a fractional increment
                await TrackerParent.UpdatePercent(parentIncrement, forceReport);

                Log($"PROGRESS PROPAGATED: command={Command.GetType().FullName}; commandId={CommandId}; commandParentId={TrackerParent.CommandId}; commandRootId={CommandRootId}; localIncrement={localPercentIncrement}; parentIncrement={parentIncrement}.");
            }
            else
            {
                // At the root level, ensure rounding only when displaying to avoid losing small increments
                await SendProgress(forceReport);
            }
        }

        private RTracker GetTrackerRoot()
        {
            if (TrackerParent == null)
                return this;
            else
                return TrackerParent.GetTrackerRoot();
        }

        private void Log(string msg)
        {
            RFacade.Log(msg);
        }
    }
}
