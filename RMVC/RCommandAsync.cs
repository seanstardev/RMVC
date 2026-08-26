using System;
using System.Threading.Tasks;

namespace RMVC {
    public abstract class RCommandAsync : RCommandBase 
    {
        protected abstract Task RunAsync();
        protected internal virtual bool EnableAutoUpdate { get; } = true;
        protected virtual string GetTitle() { return GetType().Name; }

        internal RTracker? rTracker { get; private set; } = null;
        private bool completeHandled = false;
        internal bool hasParent { get; private set; } = false;

        private string? pendingTitle = null;
        private double? pendingPercent = null;
        private string? pendingMessage = null;

        internal async Task RunInternalAsync(RTracker rTracker) 
        {
            this.rTracker = rTracker;

            rTracker.SetProgressTitle(
                pendingTitle ?? GetTitle());

            ApplyPendingProgress();

            try 
            {
                if (rTracker.Token.IsCancellationRequested) 
                {
                    SetError("Operation was cancelled.");
                    return;
                }

                await RunAsync();
            }
            catch (Exception ex) 
            {
                SetError(ex.Message);
            }
            finally 
            {
                HandleThreadExit();
            }
        }

        protected async Task ExecuteCommandAsync(
            RCommandAsync command,
            double percentCap = 100) 
        {
            command.hasParent = true;
        
            if (rTracker != null) {
                // Execute command with a scaled RTracker child using the provided percentCap
                await rTracker.facade.ExecuteCommandAsync(command, rTracker.CreateChild(command, percentCap), percentCap);
            }
        }
        
        internal override void ExecuteCommandInternal(RCommand command) 
        {
            rTracker?.facade.ExecuteCommand(command);
        }
        
        protected virtual void OnCommandExit(bool success) { }
        
        internal void HandleThreadExit() 
        {
            if (completeHandled) 
                return;

            completeHandled = true;

            if (rTracker != null) 
            {
                OnCommandExit(!rTracker.ErrorOrAbort);

                // Ensure root-level tracker sends final 100% update
                if (rTracker._parent == null) 
                {
                    rTracker.SetProgress(100, "Complete");
                }
            }
        }

        internal void SetErrorInternal(string errorMessage) 
        {
            rTracker?.SetError(errorMessage);
            HandleThreadExit();
        }

        protected void SetTitle(string title) 
        {
            if (string.IsNullOrWhiteSpace(title))
                return;

            if (rTracker != null) 
            {
                rTracker.SetProgressTitle(title);
                return;
            }

            pendingTitle = title;
        }

        protected void SetProgress(int parts, int total, string? message = null) 
        {
            SetProgress(GetPercent(parts, total), message);
        }

        protected void SetProgress(double percent, string? message = null) 
        {
            percent = RHelper.ClampPercent(percent);

            if (rTracker != null) 
            {
                rTracker.SetProgress(percent, message ?? string.Empty);
                return;
            }

            StorePendingProgress(percent, message);
        }

        protected void SetProgress(int percent, string? message = null) 
        {
            SetProgress((double)percent, message);
        }

        protected void SetProgress(string message) 
        {
            if (rTracker != null) 
            {
                rTracker.SetProgress(message);
                return;
            }

            StorePendingProgress(null, message);
        }

        protected void SetError(string? errorMessage = null) 
        {
            rTracker?.SetError(errorMessage ?? string.Empty);
        }

        protected double GetPercent(int parts, int totalParts) 
        {
            return RHelper.ClampPercent((double)parts / totalParts * 100);
        }

        protected double GetPercent(int totalParts) 
        {
            return RHelper.ClampPercent((double)totalParts / 10d);
        }

        protected string ErrorMessage { get { return rTracker?.ErrorMessage ?? string.Empty; } }
        protected bool ErrorOrAbort { get { return rTracker?.ErrorOrAbort ?? false; } }

        private void StorePendingProgress(double? percent, string? message) 
        {
            if (percent.HasValue) 
            {
                double sanitizedPercent = RHelper.ClampPercent(percent.Value);

                if (!pendingPercent.HasValue || sanitizedPercent > pendingPercent.Value)
                    pendingPercent = sanitizedPercent;
            }

            if (!string.IsNullOrWhiteSpace(message))
                pendingMessage = message;
        }

        private void ApplyPendingProgress() 
        {
            if (rTracker == null)
                return;

            if (pendingPercent.HasValue) 
            {
                rTracker.SetProgress(
                    pendingPercent.Value,
                    pendingMessage ?? string.Empty);
            }
            else if (!string.IsNullOrWhiteSpace(pendingMessage)) 
            {
                rTracker.SetProgress(pendingMessage);
            }

            pendingPercent = null;
            pendingMessage = null;
            pendingTitle = null;
        }
    }
}