using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace RMVC
{
    internal class RTracker
    {
        internal string Id { get; }
        internal string ChainId => GetRootRTracker().Id;
        private readonly object errorGate = new object();
        private readonly List<RErrorItem> errorItems = new List<RErrorItem>();

        internal bool Error
        {
            get
            {
                var root = GetRootRTracker();

                lock (root.errorGate)
                    return root.errorItems.Count > 0;
            }
        }
        internal CancellationToken Token { get; }
        internal bool ErrorOrAbort { get { return Error || Token.IsCancellationRequested; } }
        internal RCommandBase Command { get; }
        internal RFacade Facade { get; }
        internal RTracker? Parent { get; private set; }
        internal bool ProgressRequested => GetRootRTracker().progressRequested;
        private const int ProgressUpdateIntervalMilliseconds = 100;
        private readonly Stopwatch progressUpdateTimer = new Stopwatch();
        private readonly double weightInParent;
        private readonly bool reportsProgress;
        private bool progressRequested;

        internal void RequestProgress()
            => GetRootRTracker().progressRequested = true;

        private RTracker? child;

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
            Id = Guid.NewGuid().ToString();
            this.weightInParent = weightInParent;
            Command = command;
            reportsProgress = command.ReportsProgress;

            Facade = facade;
            Token = cancellationToken;
        }

        internal RProgress[] GetProgressReport()
        {
            List<RProgress> list = new List<RProgress>();
            RTracker[] trackerPath = GetAllRTrackersFlat();

            foreach (RTracker tracker in trackerPath)
            {
                if (!tracker.reportsProgress)
                    continue;

                list.Add(new RProgress(
                    Convert.ToInt32(Math.Round(tracker.localPercent)), // Final rounding here for report
                    tracker.message ?? string.Empty,
                    tracker.title ?? string.Empty,
                    tracker.Id
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
                Log($"PROGRESS SET: command={Command.GetType().FullName}; tracker={Id}; chain={ChainId}; percent={percentComplete}; increment={percentComplete - localPercent}.");
                await UpdatePercent(percentComplete - localPercent, forceReport);
            }
            else
                await SendProgress(forceReport);
        }

        internal void SetError(string errorMessage, string? exceptionDetails = null)
        {
            if (string.IsNullOrWhiteSpace(errorMessage))
                errorMessage = "Unspecified Error.";

            var root = GetRootRTracker();

            lock (root.errorGate)
            {
                root.errorItems.Insert(0, new RErrorItem(
                    errorMessage,
                    Command.GetType().FullName ?? Command.Name,
                    Id,
                    root.Id,
                    exceptionDetails));
            }
        }

        internal IReadOnlyList<RErrorItem> GetErrors()
        {
            var root = GetRootRTracker();

            lock (root.errorGate)
                return Array.AsReadOnly(root.errorItems.ToArray());
        }

        internal RErrorItem? GetLatestError()
        {
            var root = GetRootRTracker();

            lock (root.errorGate)
                return root.errorItems.Count == 0 ? null : root.errorItems[0];
        }

        internal RTracker CreateChild(RCommandBase command, double percentCap)
        {
            percentCap = RHelper.ClampPercent(percentCap);  // Ensure cap is within bounds
            
            Log($"CHILD ALLOCATED: command={command.GetType().FullName}; parent={Id}; chain={ChainId}; percentCap={percentCap}.");

            child = new RTracker(command, Facade, percentCap, Token);
            child.Parent = this;

            return child;
        }

        private RTracker[] GetAllRTrackersFlat()
        {
            List<RTracker> list = new List<RTracker>();

            RTracker rootRTracker = GetRootRTracker();
            list.Add(rootRTracker);

            RTracker? child = rootRTracker.child;

            while (child != null)
            {
                list.Add(child);
                child = child.child;
            }

            return list.ToArray();
        }

        private async Task SendProgress(bool forceReport = false)
        {
            if (!GetRootRTracker().progressRequested)
                return;

            if (Parent != null)
            {
                await Parent.SendProgress(forceReport);
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
                await Facade.HandleProgressChange(Id, progressSet).ConfigureAwait(false);
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
                    $"tracker={Id}; chain={ChainId}; accumulated={localPercent}%. " +
                    "Check child percentage allocations and direct SetProgress() calls.");
            }

            Log($"PROGRESS ACCUMULATED: command={Command.GetType().FullName}; tracker={Id}; chain={ChainId}; percent={localPercent}.");

            if (Parent != null)
            {
                // Calculate parent increment proportionally based on this tracker's cap and apply it immediately
                double parentIncrement = localPercentIncrement * (weightInParent / 100d);

                // Propagate to parent with a fractional increment
                await Parent.UpdatePercent(parentIncrement, forceReport);

                Log($"PROGRESS PROPAGATED: command={Command.GetType().FullName}; tracker={Id}; parent={Parent.Id}; chain={ChainId}; localIncrement={localPercentIncrement}; parentIncrement={parentIncrement}.");
            }
            else
            {
                // At the root level, ensure rounding only when displaying to avoid losing small increments
                await SendProgress(forceReport);
            }
        }

        private RTracker GetRootRTracker()
        {
            if (Parent == null)
                return this;
            else
                return Parent.GetRootRTracker();
        }

        private void Log(string msg)
        {
            RFacade.Log(msg);
        }
    }
}
