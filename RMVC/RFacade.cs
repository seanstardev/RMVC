using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace RMVC
{
    public abstract class RFacade
    {
        public static bool EnableDebugMode { get; set; }

        private static readonly object registryGate = new object();
        private static bool hasBeenConfigured;

        private static readonly object operationGate = new object();
        private static readonly Dictionary<CancellationTokenSource, RTracker> activeOperations =
            new Dictionary<CancellationTokenSource, RTracker>();
        private static readonly Queue<bool> operationNotifications = new Queue<bool>();
        private static bool deliveringOperationNotifications;

        private static RFacade? promoterFacade;
        private static Type? promoterType;
        private static IRAppShell? appShell;

        internal protected static IRAppShell? AppShell
        {
            get { lock (registryGate) { return appShell; } }
        }

        private readonly Dictionary<Type, RMediator> mediatorsDictionary = 
            new Dictionary<Type, RMediator>();
        private readonly Dictionary<Type, RMediator> contractMediatorsDictionary = 
            new Dictionary<Type, RMediator>();
        private readonly Dictionary<Type, IRModel> modelsDictionary = 
            new Dictionary<Type, IRModel>();

        private static readonly Dictionary<Type, RFacade> activeFacades = 
            new Dictionary<Type, RFacade>();
        private static readonly HashSet<Type> pendingFacades = 
            new HashSet<Type>();

        public static void Create<TFacade>(IRAppShell? appShell = null)
            where TFacade : RFacade, new()
        {
            var facadeConcreteType = typeof(TFacade);

            lock (registryGate)
            {
                if (activeFacades.ContainsKey(facadeConcreteType) || !pendingFacades.Add(facadeConcreteType))
                    throw new InvalidOperationException(
                        $"Facade '{facadeConcreteType.FullName}' already exists or is currently being created.");
            }

            try
            {
                var facade = new TFacade();

                var models = facade.RegisterModels();

                foreach (IRModel model in models)
                {
                    var modelType = model.GetType();

                    if (facade.modelsDictionary.ContainsKey(modelType))
                    {
                        throw new InvalidOperationException(
                            $"Facade '{facadeConcreteType.FullName}' registers model " +
                            $"'{modelType.FullName}' more than once.");
                    }

                    facade.modelsDictionary.Add(modelType, model);

                    if (model is RModel rModel)
                        rModel.Facade = facade;
                }

                var mediators = facade.RegisterMediators();

                foreach (RMediator mediator in mediators)
                {
                    var mediatorType = mediator.GetType();

                    if (facade.mediatorsDictionary.ContainsKey(mediatorType))
                    {
                        throw new InvalidOperationException(
                            $"Facade '{facadeConcreteType.FullName}' registers mediator " +
                            $"'{mediatorType.FullName}' more than once.");
                    }

                    var contractType = mediator.ActorContractType;
                    ValidateContractType(contractType);

                    if (facade.contractMediatorsDictionary.TryGetValue(
                        contractType, 
                        out var existingMediator))
                    {
                        throw new InvalidOperationException(
                            $"Facade '{facadeConcreteType.FullName}' registers contract '{contractType.FullName}' " +
                            $"with both '{existingMediator.GetType().FullName}' and '{mediator.GetType().FullName}'.");
                    }

                    facade.mediatorsDictionary.Add(mediatorType, mediator);
                    facade.contractMediatorsDictionary.Add(contractType, mediator);

                    mediator.Facade = facade;
                }

                RCommandBase startupCommand = facade.RegisterStartupCommand();

                // Publish only fully registered Facades. Never call application code under this lock.
                lock (registryGate)
                {
                    activeFacades.Add(facadeConcreteType, facade);

                    if (promoterType == facadeConcreteType || promoterFacade == null)
                        promoterFacade = facade;

                    if (RFacade.appShell == null)
                        RFacade.appShell = appShell;
                }

                facade.Execute(startupCommand);

                Log($"FACADE CREATED: facade={facadeConcreteType.FullName}; startupDispatched=true.");
            }
            catch (Exception ex)
            {
                Log($"FACADE CREATION ERROR: facade={facadeConcreteType.FullName}; {ex}");

                throw;
            }
            finally
            {
                lock (registryGate) 
                    pendingFacades.Remove(facadeConcreteType);
            }
        }

        // Resolve the exact declared contract; inherited contracts are separate registrations.
        public static void RegisterActor<TActor>(IRContract actor)
            where TActor : class, IRContract
        {
            ValidateActor<TActor>(actor);
            RegisterActorInternal(FindContractMediator(typeof(TActor)), actor);
        }

        public static void RegisterActor<TFacade, TActor>(IRContract actor)
            where TFacade : RFacade
            where TActor : class, IRContract
        {
            ValidateActor<TActor>(actor);
            RegisterActorInternal(FindContractMediator(typeof(TFacade), typeof(TActor)), actor);
        }

        public static void UnregisterActor<TActor>(IRContract actor)
            where TActor : class, IRContract
        {
            ValidateActor<TActor>(actor);
            UnregisterActorInternal(FindContractMediator(typeof(TActor)), actor);
        }

        public static void UnregisterActor<TFacade, TActor>(IRContract actor)
            where TFacade : RFacade
            where TActor : class, IRContract
        {
            ValidateActor<TActor>(actor);
            UnregisterActorInternal(FindContractMediator(typeof(TFacade), typeof(TActor)), actor);
        }

        private static void ValidateActor<TActor>(IRContract actor)
            where TActor : class, IRContract
        {
            var contractType = typeof(TActor);
            ValidateContractType(contractType);

            if (!contractType.IsInstanceOfType(actor))
            {
                throw new ArgumentException(
                    $"Actor '{actor.GetType().FullName}' does not implement " +
                    $"the requested contract '{contractType.FullName}'.",
                    nameof(actor));
            }
        }

        private static RMediator? FindContractMediator(Type contractType)
        {
            lock (registryGate)
            {
                ValidateContractType(contractType);

                RMediator? result = null;
                Type? ownerType = null;

                foreach (var entry in activeFacades)
                {
                    if (!entry.Value.contractMediatorsDictionary.TryGetValue(
                        contractType,
                        out var mediator))
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
                ValidateContractType(contractType);

                if (activeFacades.TryGetValue(facadeType, out var facade) &&
                    facade.contractMediatorsDictionary.TryGetValue(
                        contractType,
                        out var mediator))
                    return mediator;

                return null;
            }
        }

        private static void ValidateContractType(Type contractType)
        {
            if (!contractType.IsInterface)
            {
                throw new ArgumentException(
                    $"Actor contract '{contractType.FullName}' must be an interface extending IRContract. " +
                    "Specify the contract interface explicitly, for example RegisterActor<IMyView>(view) " +
                    "or UnregisterActor<IMyView>(view).");
            }
        }

        private static void RegisterActorInternal(RMediator? mediator, IRContract actor)
        {
            if (mediator == null || ReferenceEquals(mediator.ActorBase, actor))
                return;

            mediator.UpdateActorBase(actor);

            Log($"ACTOR REGISTERED: mediator={mediator.GetType().FullName}; contract={mediator.ActorContractType.FullName}; actor={actor.GetType().FullName}.");
        }

        private static void UnregisterActorInternal(RMediator? mediator, IRContract actor)
        {
            if (mediator == null || !ReferenceEquals(mediator.ActorBase, actor))
                return;

            mediator.RemoveActorBase();

            Log($"ACTOR UNREGISTERED: mediator={mediator.GetType().FullName}; contract={mediator.ActorContractType.FullName}; actor={actor.GetType().FullName}.");
        }

        public static void Configure(Type? promoterType = null)
        {
            lock (registryGate)
            {
                if (hasBeenConfigured)
                    throw new InvalidOperationException(
                        "RFacade.Configure() can only be called once.");

                if (activeFacades.Count > 0 || pendingFacades.Count > 0)
                    throw new InvalidOperationException(
                        "RFacade.Configure() must be called before any Facade creation begins.");

                if (promoterType != null &&
                    (!typeof(RFacade).IsAssignableFrom(promoterType) ||
                     promoterType.IsAbstract))
                {
                    throw new ArgumentException(
                        "The promoter type must be a concrete RFacade type.",
                        nameof(promoterType));
                }

                RFacade.promoterType = promoterType;
                hasBeenConfigured = true;
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

        public void AbortAllCommands()
        {
            CancellationTokenSource[] operations;

            lock (operationGate)
            {
                operations = new CancellationTokenSource[activeOperations.Count];
                activeOperations.Keys.CopyTo(operations, 0);
            }

            foreach (var source in operations)
            {
                try 
                { 
                    source.Cancel(); 
                }
                catch (ObjectDisposedException)
                {
                    // The operation finished after the Abort snapshot was taken.
                }
                catch (AggregateException ex)
                {
                    // A throwing cancellation callback must not prevent other workers being cancelled.
                    Log($"CANCELLATION CALLBACK ERROR: {ex}");
                }
            }
        }

        private static bool TryRegisterOperation(CancellationTokenSource source, RTracker tracker)
        {
            var commandType = tracker.Command.GetType();

            // Evaluate application overrides outside the framework lock.
            var allowConcurrentRoots = tracker.Command.AllowConcurrentRoots;
            int activeCount;
            
            lock (operationGate)
            {
                if (!allowConcurrentRoots)
                {
                    foreach (var active in activeOperations.Values)
                    {
                        if (ReferenceEquals(active.Facade, tracker.Facade) &&
                            active.Command.GetType() == commandType)
                            return false;
                    }
                }

                activeOperations.Add(source, tracker);
                activeCount = activeOperations.Count;

                if (activeOperations.Count == 1)
                    operationNotifications.Enqueue(true);

            }
            if (activeCount > 1)
                Log($"ROOTS CONCURRENT: activeCount={activeCount}.");

            DeliverOperationNotifications();

            return true;
        }

        private static void UnregisterOperation(CancellationTokenSource source)
        {
            lock (operationGate)
            {
                if (activeOperations.Remove(source) && activeOperations.Count == 0)
                    operationNotifications.Enqueue(false);
            }

            DeliverOperationNotifications();
        }

        private static void DeliverOperationNotifications()
        {
            lock (operationGate)
            {
                if (deliveringOperationNotifications || operationNotifications.Count == 0)
                    return;

                deliveringOperationNotifications = true;
            }

            while (true)
            {
                bool started;

                lock (operationGate)
                {
                    if (operationNotifications.Count == 0)
                    {
                        deliveringOperationNotifications = false;
                        return;
                    }
                    started = operationNotifications.Dequeue();
                }

                // Serialise notifications, but never call application code under the tracking lock.
                try
                {
                    var shell = AppShell;

                    if (started)
                        shell?.HandleOperationsStarted();
                    else
                        shell?.HandleOperationsCompleted();
                }
                catch (Exception ex)
                {
                    Log($"OPERATION NOTIFICATION ERROR: started={started}; {ex}");
                }
            }
        }

        internal TModel? ResolveModel<TModel>()
            where TModel : class, IRModel
        {
            if (modelsDictionary.TryGetValue(
                typeof(TModel),
                out IRModel? model))
            {
                return model as TModel;
            }

            return null;
        }

        internal TMediator? ResolveMediator<TMediator>()
            where TMediator : RMediator
        {
            if (mediatorsDictionary.TryGetValue(
                typeof(TMediator),
                out RMediator? mediator))
            {
                return mediator as TMediator;
            }

            return null;
        }

        internal void Execute(RCommandBase command, string? dispatchSource = null)
        {
            command.ClaimExecution();

            // Models and mediators dispatch roots; commands execute children directly.
            _ = DispatchCommandAsync(command, dispatchSource);
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

        private async Task DispatchCommandAsync(RCommandBase command, string? dispatchSource)
        {
            using (var source = new CancellationTokenSource())
            {
                var tracker = new RTracker(command, this, 100d, source.Token);

                bool admitted;

                try
                {
                    admitted = TryRegisterOperation(source, tracker);
                }
                catch (Exception ex)
                {
                    try
                    {
                        Log(
                            $"ROOT ADMISSION ERROR: command={command.GetType().FullName}; " +
                            $"facade={GetType().FullName}; tracker={tracker.Id}; " +
                            $"chain={tracker.ChainId}; source={dispatchSource ?? "facade"}; {ex}");
                    }
                    finally
                    {
                        UnregisterOperation(source);
                    }

                    return;
                }

                if (!admitted)
                {
                    Log(
                        $"ROOT REJECTED: command={command.GetType().FullName}; " +
                        $"facade={GetType().FullName}; tracker={tracker.Id}; chain={tracker.ChainId}; source={dispatchSource ?? "facade"}; " +
                        "another root instance is already active.");

                    return;
                }

                try
                {
                    // Only roots are scheduled; awaited children execute directly within the chain.
                    await Task.Run(async () =>
                    {
                        try
                        {
                            if (EnableDebugMode)
                                Log($"ROOT START: command={command.GetType().FullName}; source={dispatchSource ?? "facade"}; tracker={tracker.Id}; chain={tracker.ChainId}; facade={GetType().FullName}; thread={Environment.CurrentManagedThreadId}.");

                            await command.ExecuteUntypedInternal(tracker).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            Log($"ROOT EXECUTION ERROR: command={command.GetType().FullName}; tracker={tracker.Id}; chain={tracker.ChainId}; facade={GetType().FullName}; {ex}");

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
                            $"tracker={tracker.Id}; chain={tracker.ChainId}; facade={GetType().FullName}; {ex}");
                    }
                    finally
                    {
                        if (EnableDebugMode)
                            Log($"ROOT END: command={command.GetType().FullName}; tracker={tracker.Id}; chain={tracker.ChainId}; facade={GetType().FullName}; errorOrAbort={tracker.ErrorOrAbort}; error={tracker.GetLatestError()?.Message ?? string.Empty}; cancelled={tracker.Token.IsCancellationRequested}; thread={Environment.CurrentManagedThreadId}.");

                        UnregisterOperation(source);
                    }
                }
            }
        }

        // Every Facade provides its own startup command; promoter selection is independent.
        protected abstract RCommandBase RegisterStartupCommand();
        protected abstract RMediator[] RegisterMediators();
        protected abstract IRModel[] RegisterModels();

        protected TMediator? Mediator<TMediator>()
            where TMediator : RMediator
            => ResolveMediator<TMediator>();

        protected TModel? Model<TModel>()
            where TModel : class, IRModel
            => ResolveModel<TModel>();

        private static RFacade? GetPromoter()
        {
            lock (registryGate)
                return promoterFacade;
        }

        private async Task HandleRootComplete(RCommandBase command)
        {
            await command.HandleCommandComplete().ConfigureAwait(false);

            if (!command.HasParent &&
                command.ReportsProgress &&
                command.Tracker?.ProgressRequested == true)
            {
                await command.Tracker.Facade.HandleProgressComplete(
                    command.Tracker.ChainId).ConfigureAwait(false);
            }
        }

        protected RFacade()
        {
            bool creationPending;

            lock (registryGate) 
                creationPending = pendingFacades.Contains(GetType());

            if (!creationPending)
                Fatal("Do not instantiate a Facade directly. Use RFacade.Create<TFacade>().");
        }

        private async Task HandleProgressComplete(string chainId)
        {
            var promoter = GetPromoter();

            if (promoter == null)
                return;

            RCommandBase? cmd = promoter.RegisterProgressCompleteCommand(chainId);

            if (cmd != null)
                await promoter.ExecuteProgressCallback(cmd).ConfigureAwait(false);
            
        }

        internal async Task HandleProgressChange(string chainId, RProgress[] progress)
        {
            var promoter = GetPromoter();

            if (promoter == null)
                return;

            RCommandBase? cmd = promoter.RegisterProgressUpdateCommand(chainId, progress);

            if (cmd != null)
                await promoter.ExecuteProgressCallback(cmd).ConfigureAwait(false);
        }

        private async Task ExecuteProgressCallback(RCommandBase command)
        {
            command.ClaimExecution();

            command.IsProgressCallback = true;

            // Execute inline; cleanup remains available after the reporting worker is aborted.
            var tracker = new RTracker(command, this, 100d, CancellationToken.None);

            await command.ExecuteUntypedInternal(tracker).ConfigureAwait(false);

            if (tracker.ErrorOrAbort)
                Log($"PROGRESS CALLBACK ERROR: command={command.GetType().FullName}; tracker={tracker.Id}; chain={tracker.ChainId}; facade={GetType().FullName}; error={tracker.GetLatestError()?.Message ?? string.Empty}.");
        }

        protected virtual RCommandBase? RegisterProgressUpdateCommand(
            string chainId, RProgress[] progress)
            => null;
        
        protected virtual RCommandBase? RegisterProgressCompleteCommand(
            string chainId)
            => null;

        private static void Fatal(string message)
            => throw new InvalidOperationException(message);

        // Currently unused.
        private static void Error(string message)
            => Log($"FACADE ERROR: message={message}");
    }
}
