using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace RMVC
{
    public abstract class RFacade : IRFacade
    {
        public static bool EnableDebugMode { get; set; }

        private enum InitialisationState
        {
            Awaiting,
            Running,
            Initialised,
            Draining,
            Ready,
            Failed
        }

        private sealed class RootSubmission
        {
            public RCommandBase[] Commands { get; }

            public string? DispatchSource { get; }

            public RootSubmission(
                RCommandBase[] commands,
                string? dispatchSource)
            {
                Commands = commands;
                DispatchSource = dispatchSource;
            }
        }

        private sealed class FacadeInitialisationException : Exception
        {
            internal string FacadeName { get; }

            internal FacadeInitialisationException(Type facadeType, Exception error)
                : base(error.Message, error)
            {
                FacadeName = facadeType.FullName ?? facadeType.Name;
            }
        }

        private RCommandBase? initialiseCommand;
        private Type? initialiseCommandType;
        private bool acceptingChildInitialisations;
        private readonly Dictionary<Type, IRInitialisation?> childInitialisations =
            new Dictionary<Type, IRInitialisation?>();

        private RFacade? facadeParent;
        internal bool HasFacadeParent => facadeParent != null;

        private IReadOnlyList<RFacade> facadeChildren = Array.Empty<RFacade>();

        private readonly object initialisationGate = new object();

        private readonly Queue<RootSubmission> pendingCommands =
            new Queue<RootSubmission>();

        private InitialisationState initialisationState = InitialisationState.Awaiting;

        private Exception? initialisationFailure;

        private static readonly object registryGate = new object();

        private static readonly ROperationRegistry operations = new ROperationRegistry();
        private static readonly RApplicationLifecycle application = new RApplicationLifecycle(operations);
        private static readonly TaskCompletionSource<bool> startupFinished =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private static IReadOnlyList<RFacade> composition = Array.Empty<RFacade>();

        internal static IRShell AppShell => application.Shell;

        private readonly RActorRegistry actors;

        private int bridgesRegistrationStarted;

        private readonly Dictionary<Type, RBridge> bridgesChildDictionary =
            new Dictionary<Type, RBridge>();

        private static readonly Dictionary<Type, RFacade> activeFacades = 
            new Dictionary<Type, RFacade>();
        // The first Create invocation claims this runtime for its entire lifetime.
        // Cleanup and failed argument validation never release the claim.
        private static Type? facadeRootType;

        private static readonly HashSet<Type> pendingFacades = 
            new HashSet<Type>();

        public static RFacadeChildRegistration Child<TFacade>()
            where TFacade : RFacade, new()
        {
            return new RFacadeChildRegistration(
                typeof(TFacade),
                () => new TFacade());
        }

        /// <summary>
        /// Constructs a composition synchronously and starts its internal initialisation lifecycle.
        /// Only the first invocation is permitted, even if validation or startup fails.
        /// Later calls throw before changing the original runtime. No reset is provided.
        /// Invalid composition requests throw before construction. Construction or
        /// initialisation failure triggers model cleanup, then HandleStateChanged(Failed) on
        /// the required shared Shell. The host must stay alive for this lifecycle.
        /// </summary>
        public static void Create<TFacade>(
            IRShell appShell,
            params RFacadeChildRegistration[] children)
            where TFacade : RFacade, new()
        {
            lock (registryGate)
            {
                if (facadeRootType != null)
                    throw new InvalidOperationException(
                        $"RFacade.Create() may be called only once per application lifetime. " +
                        $"The first attempt selected '{facadeRootType.FullName}'. " +
                        "Declare all children in that initial composition. " +
                        "Failed startup does not permit recreation.");

                facadeRootType = typeof(TFacade);
            }

            if (appShell == null) throw new ArgumentNullException(nameof(appShell));
            if (children == null)
                throw new ArgumentNullException(nameof(children));

            var registrations = new List<RFacadeChildRegistration>
            {
                Child<TFacade>()
            };

            registrations.AddRange(children);

            var types = new HashSet<Type>();

            foreach (var registration in registrations)
            {
                if (registration == null)
                    throw new ArgumentException("A child registration cannot be null.", nameof(children));

                if (!types.Add(registration.FacadeType))
                    throw new ArgumentException("A facade cannot appear twice in a composition.", nameof(children));
            }

            lock (registryGate)
            {
                foreach (var type in types)
                    pendingFacades.Add(type);
            }

            var facades = new List<RFacade>();
            composition = facades;
            application.Begin(appShell);
            bool cleanupPending = false;
            Type buildingType = typeof(TFacade);
            string stage = "Facade constructor";

            try
            {
                foreach (var registration in registrations)
                {
                    buildingType = registration.FacadeType;
                    stage = "Facade constructor";
                    var facade = registration.Factory();
                    facades.Add(facade);
                    stage = "RegisterModels";
                    facade.actors.RegisterModels(facade.RegisterModels);
                    stage = "RegisterMediators";
                    facade.actors.RegisterMediators(facade.RegisterMediators);
                }

                facades[0].facadeChildren = facades.GetRange(1, facades.Count - 1).AsReadOnly();

                foreach (var child in facades[0].facadeChildren)
                    child.facadeParent = facades[0];

                foreach (var facade in facades)
                {
                    buildingType = facade.GetType();
                    stage = "RegisterBridges";
                    facade.InitialiseBridgesChild();
                }

                // All actors can find their facade before any initialise command runs.
                lock (registryGate)
                {
                    foreach (var facade in facades)
                    {
                        activeFacades.Add(facade.GetType(), facade);
                    }


                }

                // The root stages child requests; ordinary queues open only after full success.
                if (application.ShutdownRequested) startupFinished.TrySetResult(true);
                else _ = Task.Run(() => facades[0].InitialiseCompositionAsync());
            }
            catch (Exception error)
            {
                // Generic construction in .NET wraps the constructor's exception.
                // Preserve the application's actual cause in startup diagnostics.
                var cause = stage == "Facade constructor"
                    && error is System.Reflection.TargetInvocationException invocation
                    && invocation.InnerException != null 
                        ? invocation.InnerException 
                        : error;

                cleanupPending = true;

                _ = Task.Run(() => RecoverConstructionAsync(
                    facades, cause, buildingType.FullName ?? buildingType.Name, stage, types));
            }
            finally
            {
                if (!cleanupPending)
                    ReleaseFacadeReservations(types);
            }
        }

        private static async Task RecoverConstructionAsync(
            IReadOnlyList<RFacade> facades, Exception error, string name, string stage, IEnumerable<Type> types)
        {
            try { await HandleStartupFailureAsync(facades, error, name, stage, types).ConfigureAwait(false); }
            finally { startupFinished.TrySetResult(true); }
        }

        /// <summary>
        /// Starts terminal shutdown within one overall millisecond budget. Returns a Task
        /// that completes after cleanup or faults on timeout/cleanup failure. Repeated calls
        /// share the original Task and budget. Shell also receives Stopped/Failed.
        /// Commands must use their protected Shutdown helper to avoid awaiting their own drain.
        /// Expiry cannot forcibly stop code. No retry, restart or process-exit policy is supplied.
        /// </summary>
        public static Task Shutdown(uint timeoutMilliseconds)
            => ShutdownInternal(timeoutMilliseconds, null);

        internal static Task ShutdownInternal(uint timeoutMilliseconds, RTracker? coordinator)
        {
            if (timeoutMilliseconds == uint.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds), "The maximum timer value is reserved; supply a finite budget below uint.MaxValue.");
            return application.Shutdown(timeoutMilliseconds, async () =>
            {
                application.SetShutdownStage("waiting for startup to unwind");
                operations.AbortAllCommands();
                await startupFinished.Task.ConfigureAwait(false);
                application.SetShutdownStage("draining command work");
                operations.AbortAllCommands();
                await operations.Drain().ConfigureAwait(false);
                var failures = new List<Exception>();
                for (int index = composition.Count - 1; index >= 0; index--)
                {
                    var facade = composition[index];
                    lock (facade.initialisationGate) facade.pendingCommands.Clear();
                    application.SetShutdownStage("cleaning models in " + facade.GetType().FullName);
                    failures.AddRange(await facade.actors.CleanupModels().ConfigureAwait(false));
                }
                // Construction may have been interrupted before publication. Release only
                // successfully cleaned unpublished actors; creation itself stays consumed.
                lock (registryGate)
                {
                    if (failures.Count == 0)
                        foreach (var facade in composition)
                            if (!activeFacades.ContainsKey(facade.GetType()))
                                facade.actors.ReleaseOwnership();
                    pendingFacades.Clear();
                }
                return failures;
            }, coordinator);
        }

        private static void ReleaseFacadeReservations(IEnumerable<Type> types)
        {
            lock (registryGate)
                foreach (var type in types)
                    pendingFacades.Remove(type);
        }

        private void InitialiseBridgesChild()
        {
            if (Interlocked.Exchange(ref bridgesRegistrationStarted, 1) != 0)
                throw new InvalidOperationException(
                    $"Facade '{GetType().FullName}': RegisterBridges() registration has already been attempted. Registration is permitted once.");

            var bridges = RegisterBridges() ?? throw new InvalidOperationException(
                $"Facade '{GetType().FullName}': RegisterBridges() returned null. Return an empty array instead.");

            foreach (var bridge in bridges)
            {
                if (bridge == null)
                    throw new InvalidOperationException(
                        $"Facade '{GetType().FullName}': RegisterBridges() returned a null entry.");

                var bridgeType = bridge.GetType();

                if (bridgesChildDictionary.ContainsKey(bridgeType))
                {
                    throw new InvalidOperationException(
                        $"Facade '{GetType().FullName}' registers bridge " +
                        $"'{bridgeType.FullName}' more than once.");
                }

                bridgesChildDictionary.Add(bridgeType, bridge);

                RFacade? destination = null;
                foreach (var child in facadeChildren)
                {
                    if (child.GetType() == bridge.DestinationFacadeType)
                    {
                        destination = child;
                        break;
                    }
                }

                if (destination == null)
                    throw new InvalidOperationException(
                        $"Facade '{GetType().FullName}' registers bridge '{bridgeType.FullName}', " +
                        $"but its destination '{bridge.DestinationFacadeType.FullName}' is not a registered direct child. " +
                        "Include that child in the composition or remove the bridge registration.");

                bridge.Facade = destination;
            }
        }

        internal TBridge ResolveBridgeChild<TBridge>()
            where TBridge : RBridge
        {
            return bridgesChildDictionary.TryGetValue(
                typeof(TBridge), out var bridge)
                    ? (TBridge)bridge
                    : throw new InvalidOperationException(
                        $"Facade '{GetType().FullName}' has no registered child bridge of exact type '{typeof(TBridge).FullName}'. " +
                        "Register that bridge in RegisterBridges().");
        }

        internal static void ValidateFacadeParentContract<TContract>()
            where TContract : class, IRFacade
        {
            if (!typeof(TContract).IsInterface)
                throw new ArgumentException(
                    $"Parent facade contract '{typeof(TContract).FullName}' must be an interface inheriting IRFacade.");
        }

        internal TContract? ResolveFacadeParentByContract<TContract>()
            where TContract : class, IRFacade
        {
            ValidateFacadeParentContract<TContract>();
            return facadeParent as TContract;
        }

        internal TFacade ResolveFacadeChild<TFacade>()
            where TFacade : RFacade
        {
            foreach (var child in facadeChildren)
            {
                if (child.GetType() == typeof(TFacade))
                    return (TFacade)child;
            }

            throw new InvalidOperationException(
                $"Facade '{GetType().FullName}' has no registered direct child of exact type '{typeof(TFacade).FullName}'. " +
                "Declare that exact child type in the composition; base types, self and siblings do not match.");
        }



        /// <summary>
        /// Submits one ordered batch of independent roots in this facade.
        /// Separate calls can overlap; prefer one root per gateway invocation.
        /// </summary>
        protected void Execute(params RCommandBase[] commands)
            => Execute(commands, GetType().FullName);

        // Resolve the exact declared contract; inherited contracts are separate registrations.
        public static void RegisterActor<TActor>(IRContract actor)
            where TActor : class, IRContract
        {
            RActorRegistry.ValidateActorRegistration<TActor>(actor);
            RActorRegistry.AttachActor(FindContractMediator(typeof(TActor)), actor);
        }

        public static void RegisterActor<TFacade, TActor>(IRContract actor)
            where TFacade : RFacade
            where TActor : class, IRContract
        {
            RActorRegistry.ValidateActorRegistration<TActor>(actor);
            RActorRegistry.AttachActor(FindContractMediator(typeof(TFacade), typeof(TActor)), actor);
        }

        public static void UnregisterActor<TActor>(IRContract actor)
            where TActor : class, IRContract
        {
            RActorRegistry.ValidateActor<TActor>(actor);
            RActorRegistry.DetachActor(FindContractMediator(typeof(TActor)), actor);
        }

        public static void UnregisterActor<TFacade, TActor>(IRContract actor)
            where TFacade : RFacade
            where TActor : class, IRContract
        {
            RActorRegistry.ValidateActor<TActor>(actor);
            RActorRegistry.DetachActor(FindContractMediator(typeof(TFacade), typeof(TActor)), actor);
        }

        private static RMediator? FindContractMediator(Type contractType)
        {
            lock (registryGate)
            {
                RActorRegistry.ValidateContractType(contractType);

                RMediator? result = null;
                Type? ownerType = null;

                foreach (var entry in activeFacades)
                {
                    var mediator = entry.Value.actors.FindContractMediator(contractType);

                    if (mediator == null)
                        continue;

                    if (result != null)
                    {
                        throw new InvalidOperationException(
                            $"Contract '{contractType.FullName}' is registered by Facades '{ownerType?.FullName}' " +
                            $"and '{entry.Key.FullName}'. Specify the Facade type when registering or unregistering the actor.");
                    }

                    result = mediator;
                    ownerType = entry.Key;
                }
                return result;
            }
        }

        private static RMediator? FindContractMediator(Type facadeType, Type contractType)
        {
            lock (registryGate)
            {
                RActorRegistry.ValidateContractType(contractType);

                if (activeFacades.TryGetValue(facadeType, out var facade))
                    return facade.actors.FindContractMediator(contractType);

                return null;
            }
        }

        internal static void Log(object? logMessage)
        {
            if (!EnableDebugMode)
                return;

            logMessage = "[RMVC] " + logMessage;

            Debug.WriteLine($"{logMessage}");
            Console.WriteLine($"{logMessage}");
        }

        public static void AbortCommands<TFacade>() where TFacade : RFacade
            => operations.AbortCommands(typeof(TFacade));

        /// <summary>Requests cooperative cancellation application-wide; does not close admission or perform cleanup.</summary>
        public static void AbortAllCommands()
            => operations.AbortAllCommands();

        internal static Task<bool> AbortAllOtherCommands(
            RTracker caller,
            uint? timeoutMilliseconds)
            => operations.AbortAllOtherCommands(caller, timeoutMilliseconds);

        internal TModel ResolveModel<TModel>()
            where TModel : class, IRModel
            => actors.ResolveModel<TModel>();

        internal TMediator ResolveMediator<TMediator>()
            where TMediator : RMediator
            => actors.ResolveMediator<TMediator>();

        internal TModel? ResolveModelOrNull<TModel>()
            where TModel : class, IRModel
            => actors.ResolveModelOrNull<TModel>();

        internal TMediator? ResolveMediatorOrNull<TMediator>()
            where TMediator : RMediator
            => actors.ResolveMediatorOrNull<TMediator>();

        internal TContract? ResolveModelByContract<TContract>()
            where TContract : class
            => actors.ResolveModelByContract<TContract>();

        internal TContract? ResolveMediatorByContract<TContract>()
            where TContract : class
            => actors.ResolveMediatorByContract<TContract>();

        // Root submission is always an explicit batch; command-to-child overloads below
        // retain their tracker, typed result and shared error/cancellation semantics.
        internal void Execute(
            RCommandBase[] commands,
            string? dispatchSource)
        {
            if (commands == null)
                throw new ArgumentNullException(nameof(commands));

            var sequence = (RCommandBase[])commands.Clone();

            foreach (var command in sequence)
            {
                if (command == null)
                {
                    throw new ArgumentException(
                        "A command sequence cannot contain null entries.",
                        nameof(commands));
                }
            }

            foreach (var command in sequence)
                ValidateOrdinaryCommand(command);

            foreach (var command in sequence)
                command.ReserveForExecution();
            
            if (TryQueueCommands(sequence, dispatchSource))
                return;

            _ = DispatchSequentiallyAsync(sequence, dispatchSource);
        }

        internal Task Execute(
            RCommandBase command,
            RTracker tracker)
        {
            return command.ExecuteUntypedInternal(tracker);
        }

        internal Task<TResult> Execute<TResult>(
            RCommand<TResult> command,
            RTracker tracker)
        {
            return command.RunInternal(tracker);
        }

        internal void ValidateOrdinaryCommand(RCommandBase command)
        {
            lock (initialisationGate)
            {
                if (command.GetType() == initialiseCommandType)
                {
                    throw new InvalidOperationException(
                        $"Initialisation command '{command.GetType().FullName}' cannot be " +
                        $"submitted through ordinary execution in facade '{GetType().FullName}'. " +
                        "Return it from CreateInitialiseCommand().");
                }
            }
        }

        internal void ValidateInitialisationExecution(RCommandBase command)
        {
            lock (initialisationGate)
            {
                if (command.GetType() == initialiseCommandType
                    && !ReferenceEquals(command, initialiseCommand))
                {
                    throw new InvalidOperationException(
                        $"Command '{command.GetType().FullName}' is not the selected " +
                        $"initialisation command for facade '{GetType().FullName}'.");
                }
            }
        }

        internal void BeginInitialisationBody(RCommandBase command)
        {
            lock (initialisationGate)
            {
                if (ReferenceEquals(command, initialiseCommand))
                    acceptingChildInitialisations = true;
            }
        }

        internal void EndInitialisationBody(RCommandBase command)
        {
            lock (initialisationGate)
            {
                if (ReferenceEquals(command, initialiseCommand))
                    acceptingChildInitialisations = false;
            }
        }

        internal void StageChildInitialisation(
            RCommandBase caller, Type childType, IRInitialisation? initialisation)
        {
            lock (initialisationGate)
            {
                if (!ReferenceEquals(caller, initialiseCommand)
                    || !acceptingChildInitialisations)
                {
                    throw new InvalidOperationException(
                        $"Facade '{GetType().FullName}': InitialiseFacadeChild may only be " +
                        "called from its selected initialisation command's Run() method.");
                }

                if (caller.Tracker == null || caller.Tracker.ErrorOrAbort)
                    throw new InvalidOperationException(
                        $"Facade '{GetType().FullName}' cannot schedule child initialisation after an error or abort.");

                bool registered = false;

                foreach (var child in facadeChildren)
                {
                    if (child.GetType() == childType)
                    {
                        registered = true;
                        break;
                    }
                }

                if (!registered)
                    throw new InvalidOperationException(
                        $"Facade '{GetType().FullName}' has no direct child registered as '{childType.FullName}'.");

                if (childInitialisations.ContainsKey(childType))
                    throw new InvalidOperationException(
                        $"Facade '{GetType().FullName}' has already scheduled initialisation " +
                        $"for child '{childType.FullName}'.");

                childInitialisations.Add(childType, initialisation);
            }
        }

        private async Task InitialiseChildrenAsync(CancellationToken token)
        {
            var requests = new List<KeyValuePair<RFacade, IRInitialisation?>>();

            lock (initialisationGate)
            {
                var missing = new List<string>();

                foreach (var child in facadeChildren)
                {
                    if (!childInitialisations.TryGetValue(child.GetType(), out var payload))
                        missing.Add(child.GetType().FullName ?? child.GetType().Name);
                    else
                        requests.Add(new KeyValuePair<RFacade, IRInitialisation?>(child, payload));
                }

                if (missing.Count > 0)
                    throw new InvalidOperationException(
                        $"Facade '{GetType().FullName}' did not schedule initialisation for: " +
                        $"{string.Join(", ", missing)}. Call InitialiseFacadeChild<TChild>() " +
                        "for every registered direct child; supply a payload if required.");

                childInitialisations.Clear();
            }

            foreach (var request in requests)
            {
                token.ThrowIfCancellationRequested();

                await request.Key.InitialiseAsync(request.Value, token).ConfigureAwait(false);
            }

            token.ThrowIfCancellationRequested();
        }

        private async Task InitialiseAsync(
            IRInitialisation? initialisation, 
            CancellationToken parentToken)
        {
            try
            {
                lock (initialisationGate)
                {
                    if (initialisationState != InitialisationState.Awaiting)
                        throw new InvalidOperationException(
                            $"Facade '{GetType().FullName}' initialisation has already been requested.");

                    initialisationState = InitialisationState.Running;
                }

                parentToken.ThrowIfCancellationRequested();

                RCommandBase command = CreateInitialiseCommand(initialisation)
                    ?? throw new InvalidOperationException("CreateInitialiseCommand() returned null.");
                
                lock (initialisationGate)
                {
                    initialiseCommandType = command.GetType();
                    initialiseCommand = command;
                }

                command.ReserveForExecution();
                
                bool succeeded = await DispatchCommandAsync(
                    command, "initialisation", parentToken).ConfigureAwait(false);
                
                if (!succeeded)
                    throw initialisationFailure ?? new InvalidOperationException(
                        command.Tracker?.GetLatestError()?.Message
                        ?? $"Facade '{GetType().FullName}' initialisation was aborted or could not execute.");

                lock (initialisationGate)
                    initialisationState = InitialisationState.Initialised;
            }
            catch (FacadeInitialisationException) { throw; }
            catch (Exception error)
            {
                throw new FacadeInitialisationException(GetType(), error);
            }
            finally
            {
                lock (initialisationGate)
                {
                    acceptingChildInitialisations = false;
                    initialiseCommand = null;
                    childInitialisations.Clear();
                }
            }
        }

        private async Task InitialiseCompositionAsync()
        {
            var composition = new List<RFacade> { this };
            composition.AddRange(facadeChildren);
            
            try
            {
                await InitialiseAsync(null, CancellationToken.None).ConfigureAwait(false);
                foreach (var facade in composition)
                {
                    lock (facade.initialisationGate)
                        facade.initialisationState = InitialisationState.Draining;
                }
                if (application.Running())
                    foreach (var facade in composition)
                        _ = facade.DrainPendingCommandsAsync();
            }
            catch (Exception error)
            {
                await HandleStartupFailureAsync(
                    composition, 
                    error,
                    (error as FacadeInitialisationException)?.FacadeName
                        ?? GetType().FullName ?? GetType().Name,
                    "Initialisation").ConfigureAwait(false);
            }
            finally { startupFinished.TrySetResult(true); }
        }

        private static async Task HandleStartupFailureAsync(
            IReadOnlyList<RFacade> composition,
            Exception error,
            string facadeName,
            string stage,
            IEnumerable<Type>? unpublishedTypes = null)
        {
            if (application.ShutdownRequested) return;
            application.CloseAdmission();
            foreach (var facade in composition)
            {
                lock (facade.initialisationGate)
                {
                    facade.initialisationFailure = error;
                    facade.initialisationState = InitialisationState.Failed;
                    facade.childInitialisations.Clear();
                    facade.pendingCommands.Clear();
                }
            }

            var cleanupFailures = new List<Exception>();
            for (int index = composition.Count - 1; index >= 0; index--)
            {
                var failures = await composition[index].actors.CleanupModels().ConfigureAwait(false);
                cleanupFailures.AddRange(failures);
            }

            string message = error.Message + Environment.NewLine + "Startup stage: " + stage;
            Exception failure = error;
            if (cleanupFailures.Count > 0)
            {
                var causes = new List<Exception> { error };
                causes.AddRange(cleanupFailures);
                failure = new AggregateException(
                    "Startup failed and model cleanup reported additional failures.", causes);
                message += Environment.NewLine + Environment.NewLine + "Model cleanup failures:";
                foreach (var cleanupFailure in cleanupFailures)
                    message += Environment.NewLine + cleanupFailure.Message;
                foreach (var facade in composition)
                    lock (facade.initialisationGate)
                        facade.initialisationFailure = failure;
            }

            if (unpublishedTypes != null)
            {
                // Do not release instances whose resource cleanup may be incomplete.
                if (cleanupFailures.Count == 0)
                    foreach (var facade in composition)
                        facade.actors.ReleaseOwnership();
                ReleaseFacadeReservations(unpublishedTypes);
            }

            Log($"STARTUP FAILED: facade={facadeName}; stage={stage}; error={failure}");
            application.StartupFailed("Facade: " + facadeName + Environment.NewLine + message);
        }

        private bool TryQueueCommands(
            RCommandBase[] commands,
            string? dispatchSource)
        {
            lock (initialisationGate)
            {
                if (!application.AcceptingCommands)
                    throw new InvalidOperationException("Cannot execute commands: application startup failed or shutdown has begun.", initialisationFailure);
                if (initialisationState == InitialisationState.Failed)
                {
                    throw new InvalidOperationException(
                        "Cannot execute commands after facade startup failed.",
                        initialisationFailure);
                }

                if (initialisationState == InitialisationState.Ready)
                    return false;

                pendingCommands.Enqueue(
                    new RootSubmission(commands, dispatchSource));

                return true;
            }
        }

        private async Task DrainPendingCommandsAsync()
        {
            while (true)
            {
                RootSubmission submission;

                lock (initialisationGate)
                {
                    if (!application.AcceptingCommands) { pendingCommands.Clear(); return; }
                    if (pendingCommands.Count == 0)
                    {
                        initialisationState = InitialisationState.Ready;

                        return;
                    }

                    submission = pendingCommands.Dequeue();
                }

                await DispatchSequentiallyAsync(
                    submission.Commands,
                    submission.DispatchSource).ConfigureAwait(false);
            }
        }

        private async Task DispatchSequentiallyAsync(
            RCommandBase[] commands,
            string? dispatchSource)
        {
            foreach (var command in commands)
            {
                await DispatchCommandAsync(
                    command,
                    dispatchSource).ConfigureAwait(false);
            }
        }

        private async Task<bool> DispatchCommandAsync(
            RCommandBase command,
            string? dispatchSource,
            CancellationToken parentInitialisationToken = default)
        {
            using (var source = parentInitialisationToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(parentInitialisationToken)
                : new CancellationTokenSource())
            {
                var tracker = new RTracker(command, this, 100d, source.Token);

                bool admitted;

                try
                {
                    admitted = operations.TryRegister(source, tracker);
                }
                catch (Exception ex)
                {
                    try
                    {
                        Log(
                            $"ROOT ADMISSION ERROR: command={command.GetType().FullName}; " +
                            $"facade={GetType().FullName}; commandId={tracker.CommandId}; " +
                            $"commandRootId={tracker.CommandRootId}; source={dispatchSource ?? "facade"}; {ex}");
                    }
                    finally
                    {
                        operations.Unregister(source);
                    }

                    return false;
                }

                if (!admitted)
                {
                    Log(
                        $"ROOT REJECTED: command={command.GetType().FullName}; " +
                        $"facade={GetType().FullName}; commandId={tracker.CommandId}; commandRootId={tracker.CommandRootId}; source={dispatchSource ?? "facade"}; " +
                        "another root instance is already active.");

                    return false;
                }

                try
                {
                    // Only roots are scheduled; awaited children execute directly within the chain.
                    await Task.Run(async () =>
                    {
                        try
                        {
                            if (EnableDebugMode)
                                Log($"ROOT START: command={command.GetType().FullName}; source={dispatchSource ?? "facade"}; commandId={tracker.CommandId}; commandRootId={tracker.CommandRootId}; facade={GetType().FullName}; thread={Environment.CurrentManagedThreadId}.");

                            await command.ExecuteUntypedInternal(tracker).ConfigureAwait(false);

                            if (ReferenceEquals(command, initialiseCommand) && !tracker.ErrorOrAbort)
                                await InitialiseChildrenAsync(tracker.Token).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            Log($"ROOT EXECUTION ERROR: command={command.GetType().FullName}; commandId={tracker.CommandId}; commandRootId={tracker.CommandRootId}; facade={GetType().FullName}; {ex}");

                            if (ReferenceEquals(command, initialiseCommand))
                                initialisationFailure = ex;
                            command.SetErrorInternal(ex.Message, ex.ToString());
                        }
                    }).ConfigureAwait(false);
                }
                finally
                {
                    try
                    {
                        await HandleRootComplete(command).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        command.SetErrorInternal(ex.Message, ex.ToString());

                        Log(
                            $"ROOT COMPLETION ERROR: command={command.GetType().FullName}; " +
                            $"commandId={tracker.CommandId}; commandRootId={tracker.CommandRootId}; facade={GetType().FullName}; {ex}");
                    }
                    finally
                    {
                        if (EnableDebugMode)
                            Log($"ROOT END: command={command.GetType().FullName}; commandId={tracker.CommandId}; commandRootId={tracker.CommandRootId}; facade={GetType().FullName}; errorOrAbort={tracker.ErrorOrAbort}; error={tracker.GetLatestError()?.Message ?? string.Empty}; cancelled={tracker.Token.IsCancellationRequested}; thread={Environment.CurrentManagedThreadId}.");

                        operations.Unregister(source);
                    }
                }

                return !tracker.ErrorOrAbort;
            }
        }

        /// <summary>Creates the one-time initialiser. Its concrete type is reserved for initialisation in this facade.</summary>
        protected abstract RCommandBase CreateInitialiseCommand(IRInitialisation? initialisation);
        /// <summary>Supplies mediators during the one registration attempt. Do not call as a lookup.</summary>
        protected abstract RMediator[] RegisterMediators();
        /// <summary>Supplies models during the one registration attempt. Keep construction lightweight; do not call as a lookup.</summary>
        protected abstract IRModel[] RegisterModels();







        // Composition is flat: the parent owns progress; standalone facades own theirs.
        private RFacade ProgressOwner => facadeParent ?? this;

        private async Task HandleRootComplete(RCommandBase command)
        {
            if (!command.HasCommandParent &&
                command.ReportsProgress &&
                command.Tracker?.IsShutdownCoordinator != true &&
                command.Tracker?.ProgressRequested == true)
            {
                await command.Tracker.Facade.HandleProgressComplete(
                    command.Tracker.CommandRootId).ConfigureAwait(false);
            }
        }

        protected RFacade()
        {
            bool creationPending;

            lock (registryGate) 
                creationPending = pendingFacades.Contains(GetType());

            if (!creationPending)
                throw new InvalidOperationException(
                    "Do not instantiate a Facade directly. Use RFacade.Create<TFacade>().");

            actors = new RActorRegistry(this);
        }

        private async Task HandleProgressComplete(string commandRootId)
        {
            var owner = ProgressOwner;
            RCommandBase? command = owner.CreateProgressCompleteCommand(commandRootId);

            if (command != null)
                await owner.ExecuteProgressCallback(command).ConfigureAwait(false);
        }

        internal async Task HandleProgressChange(string commandRootId, RProgress[] progress)
        {
            var owner = ProgressOwner;
            RCommandBase? command = owner.CreateProgressUpdateCommand(commandRootId, progress);

            if (command != null)
                await owner.ExecuteProgressCallback(command).ConfigureAwait(false);
        }

        private async Task ExecuteProgressCallback(RCommandBase command)
        {
            ValidateOrdinaryCommand(command);
            command.ReserveForExecution();

            command.IsProgressCallback = true;

            // Execute inline; cleanup remains available after the reporting worker is aborted.
            var tracker = new RTracker(command, this, 100d, CancellationToken.None);

            await command.ExecuteUntypedInternal(tracker).ConfigureAwait(false);

            if (tracker.ErrorOrAbort)
                Log($"PROGRESS CALLBACK ERROR: command={command.GetType().FullName}; commandId={tracker.CommandId}; commandRootId={tracker.CommandRootId}; facade={GetType().FullName}; error={tracker.GetLatestError()?.Message ?? string.Empty}.");
        }

        /// <summary>Supplies child gateways during the one bridge registration attempt.</summary>
        protected virtual RBridge[] RegisterBridges()
            => [];

        /// <summary>Creates a fresh command for each progress update; called repeatedly, or return null to omit reporting.</summary>
        protected virtual RCommandBase? CreateProgressUpdateCommand(
            string commandRootId, RProgress[] progress)
            => null;
        
        /// <summary>Creates a fresh command for each root's reporting completion; called repeatedly, or return null.</summary>
        protected virtual RCommandBase? CreateProgressCompleteCommand(
            string commandRootId)
            => null;
    }
}
