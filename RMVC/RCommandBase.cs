using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RMVC
{
    public abstract class RCommandBase
    {
        public string Name => GetType().Name;
        protected virtual string GetProgressTitle() => GetType().Name;

        protected internal virtual bool AllowConcurrentRoots => true;

        internal RTracker? Tracker { get; private set; }
        private int executionReserved;

        // Permanently reserve this instance before queueing or running it.
        // Reservation prevents reuse; it does not mean Run() has started.
        internal void ReserveForExecution()
        {
            if (Interlocked.CompareExchange(ref executionReserved, 1, 0) == 0)
                return;

            string message =
                "Command instance has already been reserved for execution. " +
                $"Create a new {GetType().Name} instance for each execution.";

            RFacade.Log($"COMMAND REJECTED: command={GetType().FullName}; reason={message}");
            
            throw new InvalidOperationException(message);
        }

        internal bool HasParent { get; set; }

        // Progress callbacks run within the reporting chain and must not report progress recursively.
        internal bool IsProgressCallback { get; set; }
        internal bool ReportsProgress => !IsProgressCallback;

        // Constructor progress calls retain values; execution applies them before Run().
        private string? pendingTitle;
        private double? pendingPercent;
        private string? pendingMessage;

        internal RFacade? Facade { get; private set; }

        protected void AbortCommands<TFacade>() where TFacade : RFacade
            => RFacade.AbortCommands<TFacade>();

        protected void AbortAllCommands()
            => Facade?.AbortAllCommands();

        protected Task<bool> AbortAllOtherCommands(
            uint? timeoutMilliseconds = null)
        {
            if (Tracker == null)
                throw new InvalidOperationException("The command is not running.");

            return RFacade.AbortAllOtherCommands(
                Tracker,
                timeoutMilliseconds);
        }

        // The application shell is shared across Facades.
        protected IRShell? Shell => RFacade.AppShell;

        internal abstract Task ExecuteUntypedInternal(RTracker tracker);

        protected static Task<RVoid> Complete()
            => Task.FromResult(RVoid.Value);

        protected static Task<TResult> Complete<TResult>(TResult result)
            => Task.FromResult(result);

        protected TContract? GetFacadeParentByContract<TContract>()
            where TContract : class, IRFacade
            => Facade?.ResolveFacadeParentByContract<TContract>();

        protected TFacade? GetFacadeChild<TFacade>()
            where TFacade : RFacade
            => Facade?.ResolveFacadeChild<TFacade>();

        protected TBridge? ChildBridge<TBridge>()
            where TBridge : RBridge
        {
            return Facade?.ResolveChildBridge<TBridge>();
        }

        protected TModel? Model<TModel>()
            where TModel : class, IRModel
            => Facade?.ResolveModel<TModel>();

        protected TMediator? Mediator<TMediator>()
            where TMediator : RMediator
            => Facade?.ResolveMediator<TMediator>();

        protected TContract? ModelByContract<TContract>()
            where TContract : class
            => Facade?.ResolveModelByContract<TContract>();

        protected TContract? MediatorByContract<TContract>()
            where TContract : class
            => Facade?.ResolveMediatorByContract<TContract>();

        internal async Task RunInternalCore(
            RTracker tracker,
            Func<Task> run)
        {
            this.Tracker = tracker;
            this.Facade = tracker.Facade;

            try
            {
                if (!IsProgressCallback)
                    await tracker.SetProgressTitle(pendingTitle ?? GetProgressTitle()).ConfigureAwait(false);

                await ApplyPendingProgress().ConfigureAwait(false);

                if (tracker.Token.IsCancellationRequested)
                    return;

                if (RFacade.EnableDebugMode && HasParent && !IsProgressCallback)
                    RFacade.Log($"CHILD START: command={GetType().FullName}; commandId={tracker.CommandId}; commandRootId={tracker.CommandRootId}; commandParentId={tracker.Parent?.CommandId}; thread={Environment.CurrentManagedThreadId}.");

                await run().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
                when (tracker.Token.IsCancellationRequested)
            {
                if (RFacade.EnableDebugMode)
                    RFacade.Log($"COMMAND ABORTED: command={GetType().FullName}; commandId={tracker.CommandId}; commandRootId={tracker.CommandRootId}.");
            }
            catch (Exception ex)
            {
                if (RFacade.EnableDebugMode)
                    RFacade.Log($"COMMAND EXCEPTION: command={GetType().FullName}; commandId={tracker.CommandId}; commandRootId={tracker.CommandRootId}; thread={Environment.CurrentManagedThreadId}; {ex}");
                
                tracker.SetError(ex.Message, ex.ToString());
            }
            finally
            {
                await HandleCommandComplete().ConfigureAwait(false);
                if (RFacade.EnableDebugMode && HasParent && !IsProgressCallback)
                    RFacade.Log($"CHILD END: command={GetType().FullName}; commandId={tracker.CommandId}; commandRootId={tracker.CommandRootId}; errorOrAbort={tracker.ErrorOrAbort}; error={tracker.GetLatestError()?.Message ?? string.Empty}; thread={Environment.CurrentManagedThreadId}.");
            }
        }

        // Await children sequentially; they share this chain's errors and cancellation.
        protected async Task Execute(
            RCommandBase command,
            double percentCap = 100)
        {
            if (Tracker != null)
            {
                command.ReserveForExecution();
                command.HasParent = true;
                command.IsProgressCallback = IsProgressCallback;

                // Execute command with a scaled RTracker child using the provided percentCap
                await Tracker.Facade.Execute(
                    command,
                    Tracker.CreateChild(command, percentCap));
            }
        }

        protected async Task<TResult> Execute<TResult>(
            RCommand<TResult> command,
            double percentCap = 100)
        {
            if (Tracker != null)
            {
                command.ReserveForExecution();
                command.HasParent = true;
                command.IsProgressCallback = IsProgressCallback;

                // Execute command with a scaled RTracker child using the provided percentCap
                return await Tracker.Facade.Execute(
                    command,
                    Tracker.CreateChild(command, percentCap));
            }

            return default!;
        }

        // True when no error has been recorded and no cancellation requested.
        protected virtual void OnCommandExit(bool noErrorOrAbort) { }

        private async Task HandleCommandComplete()
        {
            if (Tracker != null)
            {
                OnCommandExit(!Tracker.ErrorOrAbort);

                // Finalise activated reporting. "Complete" means the chain ended, not that it succeeded.
                if (Tracker.Parent == null && !IsProgressCallback)
                    await Tracker.SetProgress(100, "Complete", forceReport: true).ConfigureAwait(false);
            }
        }

        internal void SetErrorInternal(string errorMessage, string? exceptionDetails = null)
            => Tracker?.SetError(errorMessage, exceptionDetails);

        protected async Task SetProgressTitle(string title)
        {
            if (IgnoreProgressCallbackUpdate())
                return;

            if (string.IsNullOrWhiteSpace(title))
                return;

            if (Tracker != null)
            {
                Tracker.RequestProgress();
                await Tracker.SetProgressTitle(title).ConfigureAwait(false);
                return;
            }

            pendingTitle = title;
        }

        protected async Task SetProgress(int part, int total, string? message = null)
        {
            if (IgnoreProgressCallbackUpdate())
                return;

            await SetProgress(GetPercent(part, total), message).ConfigureAwait(false);
        }

        protected async Task SetProgress(double percent, string? message = null)
        {
            if (IgnoreProgressCallbackUpdate())
                return;

            percent = RHelper.ClampPercent(percent);

            if (Tracker != null)
            {
                Tracker.RequestProgress();
                await Tracker.SetProgress(percent, message ?? string.Empty).ConfigureAwait(false);
                return;
            }

            StorePendingProgress(percent, message);
        }

        protected async Task SetProgress(int percent, string? message = null)
            => await SetProgress((double)percent, message).ConfigureAwait(false);

        protected async Task SetProgress(string message)
        {
            if (IgnoreProgressCallbackUpdate())
                return;

            if (Tracker != null)
            {
                Tracker.RequestProgress();
                await Tracker.SetProgress(message).ConfigureAwait(false);
                return;
            }

            StorePendingProgress(null, message);
        }

        protected void SetError(string? errorMessage = null)
            => Tracker?.SetError(errorMessage ?? string.Empty);

        protected double GetPercent(int part, int totalParts)
        {
            if (totalParts <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(totalParts),
                    "Total parts must be greater than zero.");

            if (part < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(part),
                    "Part must not be negative.");

            return RHelper.ClampPercent((double)part / totalParts * 100);
        }

        // Immutable items in a newest-first, read-only snapshot of this chain's history.
        protected IReadOnlyList<RErrorItem> Errors 
            => Tracker?.GetErrors() ?? Array.Empty<RErrorItem>();
        
        protected bool AbortRequested
            => Tracker?.Token.IsCancellationRequested ?? false;

        protected void ThrowIfAbortRequested()
        {
            if (AbortRequested)
                throw new OperationCanceledException();
        }

        protected bool ErrorOrAbort 
            => Tracker?.ErrorOrAbort ?? false;

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

        private bool IgnoreProgressCallbackUpdate()
        {
            if (!IsProgressCallback)
                return false;
        
            RFacade.Log($"PROGRESS IGNORED: command={GetType().FullName}; commandId={Tracker?.CommandId}; commandRootId={Tracker?.CommandRootId}; reason=framework progress callbacks cannot report their own progress.");
            
            return true;
        }

        private async Task ApplyPendingProgress()
        {
            if (IsProgressCallback)
            {
                if (pendingPercent.HasValue || pendingMessage != null || pendingTitle != null)
                    IgnoreProgressCallbackUpdate();
                
                pendingPercent = null;
                pendingMessage = null;
                pendingTitle = null;
                
                return;
            }
            if (Tracker == null)
                return;

            if (pendingPercent.HasValue)
            {
                Tracker.RequestProgress();
                
                await Tracker.SetProgress(
                    pendingPercent.Value,
                    pendingMessage ?? string.Empty);
            }
            else if (!string.IsNullOrWhiteSpace(pendingMessage))
            {
                Tracker.RequestProgress();
                await Tracker.SetProgress(pendingMessage!).ConfigureAwait(false);
            }
            else if (!string.IsNullOrWhiteSpace(pendingTitle))
            {
                Tracker.RequestProgress();
                await Tracker.SetProgressTitle(pendingTitle!).ConfigureAwait(false);
            }

            pendingPercent = null;
            pendingMessage = null;
            pendingTitle = null;
        }
    }
}