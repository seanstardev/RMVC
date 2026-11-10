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

        internal bool HasCommandParent { get; set; }

        // Progress callbacks run within the reporting chain and must not report progress recursively.
        internal bool IsProgressCallback { get; set; }
        internal bool ReportsProgress => !IsProgressCallback;

        // Constructor progress calls retain values; execution applies them before Run().
        private string? pendingTitle;
        private double? pendingPercent;
        private string? pendingMessage;

        internal RFacade? Facade { get; private set; }

        protected void AbortCommands<TFacade>() where TFacade : RFacade
        {
            RequireActorAccess();
            RFacade.AbortCommands<TFacade>();
        }

        /// <summary>Requests cancellation of admitted roots of this exact type in the command's facade.</summary>
        protected void AbortRootCommands<TRootCommand>()
            where TRootCommand : RCommandBase
        {
            RequireActorAccess();

            var facade = Facade ?? throw new InvalidOperationException("The command is not running.");
            RFacade.AbortRootCommands(facade.GetType(), typeof(TRootCommand));
        }

        protected void AbortAllCommands()
        {
            RequireActorAccess();
            if (Facade != null)
                RFacade.AbortAllCommands();
        }

        protected Task<bool> AbortAllOtherCommands(
            uint? timeoutMilliseconds = null)
        {
            RequireActorAccess();
            if (Tracker == null)
                throw new InvalidOperationException("The command is not running.");

            return RFacade.AbortAllOtherCommands(
                Tracker,
                timeoutMilliseconds);
        }

        /// <summary>Terminal shutdown: preserves this root for Shell-only completion. Faults on timeout or cleanup failure.</summary>
        /// <remarks>Only after full initialisation. After calling, do not use actors, cached actor references, delegates or actor-backed logging; only Shell interaction and local control flow remain valid.</remarks>
        protected Task Shutdown(uint milliseconds)
        {
            var tracker = Tracker ?? throw new InvalidOperationException("Shutdown requires a running command.");
            return RFacade.ShutdownInternal(milliseconds, tracker);
        }

        private void RequireActorAccess()
        {
            if (Tracker?.IsShutdownCoordinator == true)
                throw new InvalidOperationException(
                    $"Command '{GetType().FullName}' belongs to the shutdown-coordinating root. " +
                    "Actor lookup and child execution are forbidden after Shutdown starts; only Shell interaction and local control flow are permitted.");
        }

        // The application shell is shared across Facades.
        protected IRShell Shell => Facade != null ? RFacade.AppShell
            : throw new InvalidOperationException("Shell requires a command bound to a facade.");

        internal abstract Task ExecuteUntypedInternal(RTracker tracker);

        protected static Task<RVoid> Complete()
            => Task.FromResult(RVoid.Value);

        protected static Task<TResult> Complete<TResult>(TResult result)
            => Task.FromResult(result);

        /// <summary>True when composition assigned this facade a direct parent.</summary>
        protected bool HasFacadeParent => Facade?.HasFacadeParent
            ?? throw new InvalidOperationException("HasFacadeParent requires a command bound to a facade.");

        /// <summary>Stages one direct child's initialisation from the selected initialiser's Run body.</summary>
        protected void InitialiseFacadeChild<TChild>(IRInitialisation? initialisation = null)
            where TChild : RFacade
        {
            RequireActorAccess();
            if (Facade == null)
                throw new InvalidOperationException("Child initialisation requires a command bound to a facade.");
            Facade.StageChildInitialisation(this, typeof(TChild), initialisation);
        }

        /// <summary>Returns the immediate parent's interface capability, or null when unbound, absent or incompatible; rejects class arguments.</summary>
        protected TContract? FacadeParentByContract<TContract>()
            where TContract : class, IRFacade
        {
            RequireActorAccess();
            RFacade.ValidateFacadeParentContract<TContract>();
            return Facade?.ResolveFacadeParentByContract<TContract>();
        }

        /// <summary>Returns the registered direct child of exactly this facade type; throws when unbound or absent.</summary>
        protected TFacade FacadeChild<TFacade>()
            where TFacade : RFacade
        {
            RequireActorAccess();
            if (Facade == null)
                throw new InvalidOperationException(
                    $"Command '{GetType().FullName}' is not bound to a facade; " +
                    $"cannot resolve direct child facade '{typeof(TFacade).FullName}'.");

            return Facade.ResolveFacadeChild<TFacade>();
        }

        /// <summary>Returns the bridge of exactly this registered type; throws when unbound or absent.</summary>
        protected TBridge BridgeChild<TBridge>()
            where TBridge : RBridge
        {
            RequireActorAccess();
            if (Facade == null)
                throw new InvalidOperationException(
                    $"Command '{GetType().FullName}' is not bound to a facade; " +
                    $"cannot resolve child bridge '{typeof(TBridge).FullName}'.");

            return Facade.ResolveBridgeChild<TBridge>();
        }

        /// <summary>Returns the unique matching model, including subclasses; throws when unbound, absent or ambiguous.</summary>
        protected TModel Model<TModel>()
            where TModel : class, IRModel
        {
            RequireActorAccess();
            if (Facade == null)
                throw new InvalidOperationException(
                    $"Command '{GetType().FullName}' is not bound to a facade; " +
                    $"cannot resolve model '{typeof(TModel).FullName}'.");

            return Facade.ResolveModel<TModel>();
        }

        /// <summary>Returns the unique matching mediator, including subclasses; throws when unbound, absent or ambiguous.</summary>
        protected TMediator Mediator<TMediator>()
            where TMediator : RMediator
        {
            RequireActorAccess();
            if (Facade == null)
                throw new InvalidOperationException(
                    $"Command '{GetType().FullName}' is not bound to a facade; " +
                    $"cannot resolve mediator '{typeof(TMediator).FullName}'.");

            return Facade.ResolveMediator<TMediator>();
        }

        /// <summary>Returns the unique matching model, including subclasses, or null when unbound, absent or ambiguous.</summary>
        protected TModel? ModelOrNull<TModel>()
            where TModel : class, IRModel
        {
            RequireActorAccess();
            return Facade?.ResolveModelOrNull<TModel>();
        }

        /// <summary>Returns the unique matching mediator, including subclasses, or null when unbound, absent or ambiguous.</summary>
        protected TMediator? MediatorOrNull<TMediator>()
            where TMediator : RMediator
        {
            RequireActorAccess();
            return Facade?.ResolveMediatorOrNull<TMediator>();
        }

        /// <summary>Returns the unique interface match, or null; rejects class arguments.</summary>
        protected TContract? ModelByContract<TContract>()
            where TContract : class
        {
            RequireActorAccess();
            RActorRegistry.ValidateLookupContract<TContract>();
            return Facade?.ResolveModelByContract<TContract>();
        }

        /// <summary>Returns the unique interface match, or null; rejects class arguments.</summary>
        protected TContract? MediatorByContract<TContract>()
            where TContract : class
        {
            RequireActorAccess();
            RActorRegistry.ValidateLookupContract<TContract>();
            return Facade?.ResolveMediatorByContract<TContract>();
        }

        internal async Task RunInternalCore(
            RTracker tracker,
            Func<Task> run)
        {
            this.Tracker = tracker;
            this.Facade = tracker.Facade;

            try
            {
                tracker.Facade.ValidateInitialisationExecution(this);
                if (!IsProgressCallback)
                    await tracker.SetProgressTitle(pendingTitle ?? GetProgressTitle()).ConfigureAwait(false);

                await ApplyPendingProgress().ConfigureAwait(false);

                if (tracker.Token.IsCancellationRequested)
                    return;

                if (RFacade.EnableDebugMode && HasCommandParent && !IsProgressCallback)
                    RFacade.Log($"CHILD START: command={GetType().FullName}; commandId={tracker.CommandId}; commandRootId={tracker.CommandRootId}; commandParentId={tracker.TrackerParent?.CommandId}; thread={Environment.CurrentManagedThreadId}.");

                tracker.Facade.BeginInitialisationBody(this);
                try
                {
                    await run().ConfigureAwait(false);
                }
                finally
                {
                    tracker.Facade.EndInitialisationBody(this);
                }
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
                if (RFacade.EnableDebugMode && HasCommandParent && !IsProgressCallback)
                    RFacade.Log($"CHILD END: command={GetType().FullName}; commandId={tracker.CommandId}; commandRootId={tracker.CommandRootId}; errorOrAbort={tracker.ErrorOrAbort}; error={tracker.GetLatestError()?.Message ?? string.Empty}; thread={Environment.CurrentManagedThreadId}.");
            }
        }

        // Await children sequentially; they share this chain's errors and cancellation.
        // Keep these entry points synchronous so misuse also throws before a Task is returned.
        protected Task Execute(
            RCommandBase command,
            double percentCap = 100)
        {
            var tracker = RequireChildExecutionTracker();
            if (command == null) throw new ArgumentNullException(nameof(command));
            tracker.Facade.ValidateOrdinaryCommand(command);
            command.ReserveForExecution();
            command.HasCommandParent = true;
            command.IsProgressCallback = IsProgressCallback;

            return tracker.Facade.Execute(
                command,
                tracker.CreateTrackerChild(command, percentCap));
        }

        protected Task<TResult> Execute<TResult>(
            RCommand<TResult> command,
            double percentCap = 100)
        {
            var tracker = RequireChildExecutionTracker();
            if (command == null) throw new ArgumentNullException(nameof(command));
            tracker.Facade.ValidateOrdinaryCommand(command);
            command.ReserveForExecution();
            command.HasCommandParent = true;
            command.IsProgressCallback = IsProgressCallback;

            return tracker.Facade.Execute(
                command,
                tracker.CreateTrackerChild(command, percentCap));
        }

        private RTracker RequireChildExecutionTracker()
        {
            RequireActorAccess();
            return Tracker ?? throw new InvalidOperationException(
                $"Command '{GetType().FullName}' cannot execute a child command before " +
                "it is bound for execution. Do not call Execute() from a command constructor.");
        }

        // True when no error has been recorded and no cancellation requested.
        protected virtual void OnCommandExit(bool noErrorOrAbort) { }

        private async Task HandleCommandComplete()
        {
            if (Tracker != null)
            {
                OnCommandExit(!Tracker.ErrorOrAbort);

                // Finalise activated reporting. "Complete" means the chain ended, not that it succeeded.
                if (Tracker.TrackerParent == null && !IsProgressCallback)
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

        /// <summary>
        /// Hides this chain's progress through the owning facade and waits for its callback.
        /// A subsequent explicit progress report can show it again.
        /// </summary>
        protected Task HideProgress()
        {
            if (IgnoreProgressCallbackUpdate())
                return Task.CompletedTask;

            return Tracker?.HideProgress() ?? Task.CompletedTask;
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