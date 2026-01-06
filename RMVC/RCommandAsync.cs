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

        internal async Task RunInternalAsync(RTracker rTracker) 
        {
            this.rTracker = rTracker;
            rTracker.SetProgressTitle(GetTitle());

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
        protected void SetProgress(int parts, int total, string? message = null) 
        {
            rTracker?.SetProgress(parts, total, message ?? string.Empty);
        }
        protected void SetProgress(double percent, string? message = null) 
        {
            rTracker?.SetProgress(percent, message ?? string.Empty);
        }
        protected void SetProgress(int percent, string? message = null) 
        {
            rTracker?.SetProgress(percent, message ?? string.Empty);
        }
        protected void SetProgress(string message) 
        {
            rTracker?.SetProgress(message);
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
    }
}