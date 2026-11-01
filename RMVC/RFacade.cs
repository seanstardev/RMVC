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

        private RFacade? parentFacade;

        private IReadOnlyList<RFacade> childFacades = Array.Empty<RFacade>();

        private readonly object initialisationGate = new object();

        private readonly Queue<RootSubmission> pendingCommands =
            new Queue<RootSubmission>();

        private InitialisationState initialisationState = InitialisationState.Awaiting;

        private Exception? initialisationFailure;

        private static readonly object registryGate = new object();

        private static readonly ROperationRegistry operations =
            new ROperationRegistry(NotifyOperationState);

        private static IRShell? appShell;

        internal protected static IRShell? AppShell
        {
            get { lock (registryGate) { return appShell; } }
        }

        private readonly RActorRegistry actors;

        private readonly Dictionary<Type, RBridge> childBridgesDictionary =
            new Dictionary<Type, RBridge>();

        private static readonly Dictionary<Type, RFacade> activeFacades = 
            new Dictionary<Type, RFacade>();
        private static readonly HashSet<Type> pendingFacades = 
            new HashSet<Type>();

        public static RFacadeChildRegistration Child<TFacade>()
            where TFacade : RFacade, new()
        {
            return new RFacadeChildRegistration(
                typeof(TFacade),
                () => new TFacade());
        }

        public static void Create<TFacade>(
            IRShell? appShell = null,
            params RFacadeChildRegistration[] children)
            where TFacade : RFacade, new()
        {
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
                {
                    if (activeFacades.ContainsKey(type) || pendingFacades.Contains(type))
                    {
                        throw new InvalidOperationException(
                            $"Facade '{type.FullName}' already exists or is currently being created.");
                    }
                }

                foreach (var type in types)
                    pendingFacades.Add(type);
            }

            try
            {
                var facades = new List<RFacade>();

                foreach (var registration in registrations)
                    facades.Add(BuildFacade(registration));

                facades[0].childFacades = facades.GetRange(1, facades.Count - 1).AsReadOnly();

                foreach (var child in facades[0].childFacades)
                    child.parentFacade = facades[0];

                foreach (var facade in facades)
                    facade.InitialiseChildBridges();

                // All actors can find their facade before any initialise command runs.
                lock (registryGate)
                {
                    foreach (var facade in facades)
                    {
                        activeFacades.Add(facade.GetType(), facade);
                    }

                    if (RFacade.appShell == null)
                        RFacade.appShell = appShell;
                }

                // Children stay Awaiting until explicitly initialised by their host.
                _ = facades[0].Initialise(null);
            }
            catch (Exception error)
            {
                Log($"FACADE CREATION ERROR: {error}");

                throw;
            }
            finally
            {
                lock (registryGate)
                {
                    foreach (var type in types)
                        pendingFacades.Remove(type);
                }
            }
        }

        private static RFacade BuildFacade(RFacadeChildRegistration registration)
        {
            RFacade facade = registration.Factory();

            facade.actors.RegisterModels(facade.RegisterModels());
            facade.actors.RegisterMediators(facade.RegisterMediators());

            return facade;
        }

        private void InitialiseChildBridges()
        {
            foreach (var bridge in RegisterBridges())
            {
                var bridgeType = bridge.GetType();

                if (childBridgesDictionary.ContainsKey(bridgeType))
                {
                    throw new InvalidOperationException(
                        $"Facade '{GetType().FullName}' registers bridge " +
                        $"'{bridgeType.FullName}' more than once.");
                }

                childBridgesDictionary.Add(bridgeType, bridge);

                foreach (var child in childFacades)
                {
                    if (child.GetType() == bridge.DestinationFacadeType)
                    {
                        bridge.Facade = child;
                        break;
                    }
                }

                if (bridge.Facade == null)
                {
                    Log(
                        $"BRIDGE UNCONNECTED: bridge={bridgeType.FullName}; " +
                        $"destination={bridge.DestinationFacadeType.FullName}.");
                }
            }
        }

        internal TBridge? ResolveChildBridge<TBridge>()
            where TBridge : RBridge
        {
            return childBridgesDictionary.TryGetValue(
                typeof(TBridge), out var bridge)
                    ? (TBridge)bridge
                    : null;
        }

        internal TContract? ResolveFacadeParentByContract<TContract>()
            where TContract : class, IRFacade
        {
            if (!typeof(TContract).IsInterface)
                return null;

            return parentFacade as TContract;
        }

        internal TFacade? ResolveFacadeChild<TFacade>()
            where TFacade : RFacade
        {
            foreach (var child in childFacades)
            {
                if (child.GetType() == typeof(TFacade))
                    return (TFacade)child;
            }

            return null;
        }

        protected TContract? GetFacadeParentByContract<TContract>()
            where TContract : class, IRFacade
            => ResolveFacadeParentByContract<TContract>();

        protected TFacade? GetFacadeChild<TFacade>()
            where TFacade : RFacade
            => ResolveFacadeChild<TFacade>();

        protected void Execute(RCommandBase command)
            => Execute(command, GetType().FullName);

        // Resolve the exact declared contract; inherited contracts are separate registrations.
        public static void RegisterActor<TActor>(IRContract actor)
            where TActor : class, IRContract
        {
            RActorRegistry.ValidateActor<TActor>(actor);
            RActorRegistry.AttachActor(FindContractMediator(typeof(TActor)), actor);
        }

        public static void RegisterActor<TFacade, TActor>(IRContract actor)
            where TFacade : RFacade
            where TActor : class, IRContract
        {
            RActorRegistry.ValidateActor<TActor>(actor);
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

        public void AbortAllCommands()
            => operations.AbortAllCommands();

        internal static Task<bool> AbortAllOtherCommands(
            RTracker caller,
            uint? timeoutMilliseconds)
            => operations.AbortAllOtherCommands(caller, timeoutMilliseconds);

        private static void NotifyOperationState(bool started)
        {
            var shell = AppShell;

            if (started)
                shell?.HandleOperationsStarted();
            else
                shell?.HandleOperationsCompleted();
        }

        internal TModel? ResolveModel<TModel>()
            where TModel : class, IRModel
            => actors.ResolveModel<TModel>();

        internal TMediator? ResolveMediator<TMediator>()
            where TMediator : RMediator
            => actors.ResolveMediator<TMediator>();

        internal TContract? ResolveModelByContract<TContract>()
            where TContract : class
            => actors.ResolveModelByContract<TContract>();

        internal TContract? ResolveMediatorByContract<TContract>()
            where TContract : class
            => actors.ResolveMediatorByContract<TContract>();

        internal void Execute(RCommandBase command, string? dispatchSource = null)
        {
            command.ReserveForExecution();

            if (TryQueueCommands(new[] { command }, dispatchSource))
                return;

            // Models and mediators dispatch roots; commands execute children directly.
            _ = DispatchCommandAsync(command, dispatchSource);
        }

        internal void ExecuteSequentially(
            RCommandBase[] commands,
            string? dispatchSource = null)
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
            {
                command.ReserveForExecution();
            }

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

        public Task Initialise(object? initialisation)
        {
            lock (initialisationGate)
            {
                if (initialisationState != InitialisationState.Awaiting)
                {
                    throw new InvalidOperationException(
                        "Facade initialisation has already been requested.");
                }

                initialisationState = InitialisationState.Running;
            }

            return InitialiseAsync(initialisation);
        }

        private async Task InitialiseAsync(object? initialisation)
        {
            try
            {
                RCommandBase command = CreateInitialiseCommand(initialisation)
                    ?? throw new InvalidOperationException("An initialise command is required.");

                command.ReserveForExecution();

                bool succeeded = await DispatchCommandAsync(
                    command,
                    "initialisation").ConfigureAwait(false);

                if (!succeeded)
                {
                    string message = command.Tracker?.GetLatestError()?.Message
                        ?? "Facade initialisation was aborted or could not execute.";

                    throw new InvalidOperationException(message);
                }

                lock (initialisationGate)
                {
                    initialisationState = InitialisationState.Draining;
                }
            }
            catch (Exception error)
            {
                lock (initialisationGate)
                {
                    initialisationFailure = error;
                    initialisationState = InitialisationState.Failed;
                    pendingCommands.Clear();
                }

                try
                {
                    AppShell?.HandleInitialisationFailed(
                        GetType().FullName ?? GetType().Name,
                        error.Message);
                }
                catch (Exception notificationError)
                {
                    Log($"INITIALISATION NOTIFICATION ERROR: {notificationError}");
                }

                throw;
            }

            _ = DrainPendingCommandsAsync();
        }

        private bool TryQueueCommands(
            RCommandBase[] commands,
            string? dispatchSource)
        {
            lock (initialisationGate)
            {
                if (initialisationState == InitialisationState.Failed)
                {
                    throw new InvalidOperationException(
                        "Cannot execute commands after facade initialisation failed.",
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
            string? dispatchSource)
        {
            using (var source = new CancellationTokenSource())
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
                        }
                        catch (Exception ex)
                        {
                            Log($"ROOT EXECUTION ERROR: command={command.GetType().FullName}; commandId={tracker.CommandId}; commandRootId={tracker.CommandRootId}; facade={GetType().FullName}; {ex}");

                            command.SetErrorInternal("RFacade Execution Error.", ex.ToString());
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

        // Every Facade provides its own initialise command.
        protected abstract RCommandBase CreateInitialiseCommand(object? initialisation);
        protected abstract RMediator[] RegisterMediators();
        protected abstract IRModel[] RegisterModels();

        protected TMediator? Mediator<TMediator>()
            where TMediator : RMediator
            => ResolveMediator<TMediator>();

        protected TModel? Model<TModel>()
            where TModel : class, IRModel
            => ResolveModel<TModel>();

        protected TContract? MediatorByContract<TContract>()
            where TContract : class
            => ResolveMediatorByContract<TContract>();

        protected TContract? ModelByContract<TContract>()
            where TContract : class
            => ResolveModelByContract<TContract>();

        // Composition is flat: the parent owns progress; standalone facades own theirs.
        private RFacade ProgressOwner => parentFacade ?? this;

        private async Task HandleRootComplete(RCommandBase command)
        {
            if (!command.HasParent &&
                command.ReportsProgress &&
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
            command.ReserveForExecution();

            command.IsProgressCallback = true;

            // Execute inline; cleanup remains available after the reporting worker is aborted.
            var tracker = new RTracker(command, this, 100d, CancellationToken.None);

            await command.ExecuteUntypedInternal(tracker).ConfigureAwait(false);

            if (tracker.ErrorOrAbort)
                Log($"PROGRESS CALLBACK ERROR: command={command.GetType().FullName}; commandId={tracker.CommandId}; commandRootId={tracker.CommandRootId}; facade={GetType().FullName}; error={tracker.GetLatestError()?.Message ?? string.Empty}.");
        }

        protected virtual RBridge[] RegisterBridges()
            => [];

        protected virtual RCommandBase? CreateProgressUpdateCommand(
            string commandRootId, RProgress[] progress)
            => null;
        
        protected virtual RCommandBase? CreateProgressCompleteCommand(
            string commandRootId)
            => null;

    }
}
