using System;
using System.Collections.Generic;
using System.Threading;

namespace RMVC 
{
    internal class RTracker 
    {
        internal string Id { get; private set; }
        internal bool Abort { get { return _abort; } set { ApplyAbort(value); } }
        internal string ErrorMessage { get; private set; } = string.Empty;
        internal bool Error { get { return _error; } }
        internal CancellationToken Token { get; private set; }
        internal bool ErrorOrAbort { get { return _error || _abort; } }
        internal RCommandAsync _command;
        internal RFacade facade;
        internal RTracker? _parent;

        private readonly double _cap;
        private readonly bool _allowAutoUpdate;

        private RTracker? _child;

        private double _localPercent;

        private string? _title;
        private string? _text;
        private bool _abort;
        private bool _error;

        internal RTracker(RCommandAsync command, RFacade facade, double cap, CancellationToken cancellationToken) 
        {
            Id = Guid.NewGuid().ToString();
            _cap = cap;
            _parent = null;
            _child = null;
            _localPercent = 0d;
            _command = command;
            _allowAutoUpdate = command.EnableAutoUpdate;

            this.facade = facade;
            _text = null;
            _title = null;
            _abort = false;
            _error = false;
            Token = cancellationToken;
        }

        internal RProgress[] GetProgressReport() 
        {
            List<RProgress> list = new List<RProgress>();
            RTracker[] rTrackerSet = GetAllRTrackersFlat();
            foreach (RTracker tracker in rTrackerSet) {
                
                if (tracker != this && tracker._localPercent == 0) 
                    continue;

                if (!tracker._allowAutoUpdate)
                    continue;

                list.Add(new RProgress(
                    Convert.ToInt32(Math.Round(tracker._localPercent)), // Final rounding here for report
                    tracker._text ?? string.Empty,
                    tracker._title ?? string.Empty,
                    tracker.Id
                ));
            }

            return list.ToArray();
        }

        internal void SetProgressTitle(string title) 
        {
            _title = title;
            SendProgress();
        }

        internal void SetProgress(string text) 
        {
            if (!string.IsNullOrWhiteSpace(text))
                _text = text;
        
            SendProgress();
        }

        internal void SetProgress(double percentComplete, string? text = null) 
        {
            percentComplete = RHelper.ClampPercent(percentComplete);
            
            if (percentComplete <= _localPercent) 
                return;

            if (!string.IsNullOrWhiteSpace(text))
                _text = text;

            Log($"Setting progress in {Id}: Local percent: {_localPercent}, Increment: {percentComplete - _localPercent}");
            UpdatePercent(percentComplete - _localPercent);
        }

        internal void SetProgress(int percentComplete, string? text = null) 
        {
            percentComplete = SanitizePercent(percentComplete);
            
            if (percentComplete <= _localPercent) 
                return;

            if (!string.IsNullOrWhiteSpace(text))
                _text = text;

            UpdatePercent(percentComplete - _localPercent);
        }

        internal void SetProgress(int step, int total, string? text = null) 
        {
            double adjusted = GetPercent(step, total);
            
            if (adjusted <= _localPercent) 
                return;

            if (!string.IsNullOrWhiteSpace(text))
                _text = text;

            UpdatePercent(adjusted - _localPercent);
        }

        internal void SetError(string errorMessage) 
        {
            if (string.IsNullOrWhiteSpace(errorMessage))
                errorMessage = "Unspecified Error.";

            _error = true;
            ErrorMessage = errorMessage;
            RTracker[] arr = GetAllRTrackersFlat();

            foreach (RTracker item in arr) 
            {
                item._error = true;
                item.ErrorMessage = ErrorMessage;
            }
        }

        internal RTracker CreateChild(RCommandAsync command, double percentCap) 
        {
            percentCap = RHelper.ClampPercent(percentCap);  // Ensure cap is within bounds
            Log($"Creating child tracker for {command.GetType().Name} with cap: {percentCap}");

            _child = new RTracker(command, facade, percentCap * (_cap / 100d), Token);
            _child._parent = this;

            return _child;
        }

        private void ApplyAbort(bool abort) 
        {
            if (!abort) 
                return;
            
            RTracker[] arr = GetAllRTrackersFlat();

            foreach (RTracker item in arr) 
            {
                item._abort = abort;
            }
        }

        private RTracker[] GetAllRTrackersFlat() 
        {
            List<RTracker> list = new List<RTracker>();
            RTracker rootRTracker = GetRootRTracker();
            list.Add(rootRTracker);

            RTracker? child = rootRTracker._child;
            while (child != null) 
            {
                list.Add(child);
                child = child._child;
            }

            return list.ToArray();
        }

        private void SendProgress() 
        {
            if (_parent != null) 
            {
                _parent.SendProgress();
                return;
            }

            var progressSet = GetProgressReport();

            if (progressSet.Length > 0) 
            {
                facade.HandleProgressChange(progressSet);
            }
        }

        private void UpdatePercent(double localPercentIncrement) 
        {    
            // Accumulate local percent without rounding for internal accuracy
            _localPercent += localPercentIncrement;

            Log($"Updating percent in {Id}: New local percent: {_localPercent}");

            if (_parent != null) 
            {
                // Calculate parent increment proportionally based on this tracker's cap and apply it immediately
                double parentIncrement = localPercentIncrement * (_cap / 100d);

                // Propagate to parent with a fractional increment
                _parent.UpdatePercent(parentIncrement);

                Log($"Propagating to parent in {Id}: Local increment = {localPercentIncrement}, Parent increment = {parentIncrement}");
            }
            else 
            {
                // At the root level, ensure rounding only when displaying to avoid losing small increments
                SendProgress();
            }
        }

        private uint GetPercent(int current, int total) 
        {
            int percent = (int)((current / (double)total) * 100);
            
            if (percent > 100) 
                percent = 100;
            else if (percent < 0) 
                percent = 0;
            
            return (uint)percent;
        }

        private int SanitizePercent(int percent) 
        {
            if (percent > 100) 
                return 100;
            else if (percent < 0) 
                return 0;
            else 
                return percent;
        }

        private RTracker GetBaseRTracker() 
        {
            if (_child == null) 
                return this;
            else 
                return _child.GetBaseRTracker();
        }

        private RTracker GetRootRTracker() 
        {
            if (_parent == null) 
                return this;
            else 
                return _parent.GetRootRTracker();
        }

        private int ToInt(double val) 
        {
            return Convert.ToInt32(val);
        }
        private void Log(string msg) 
        {
            RFacade.Log("[RTracker] " + msg);
        }
    }
}
