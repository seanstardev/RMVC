using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace RMVC
{
    public abstract class RFacade 
    {
        public static bool EnableDebugMode { get; set; } = false;
        public static bool EnableTestMode { get; set; } = false;

        private static bool hasBeenConfigured = false;

        private RCommander? rCommander = null;
        
        private DateTime lastProgressUpdateTime = DateTime.MinValue;

        private static RFacade? promoterFacade = null;
        private static Type? promoterType = null;
        internal protected static IRAppShell? AppShell { get; private set; } = null;

        private readonly ConcurrentDictionary<Task, CancellationTokenSource> activeTasks =
           new ConcurrentDictionary<Task, CancellationTokenSource>();

        private readonly Dictionary<Type, RMediator> mediatorsDictionary = new Dictionary<Type, RMediator>();
        private readonly Dictionary<Type, RModel> modelsDictionary = new Dictionary<Type, RModel>();

        private static readonly Dictionary<Type, RFacade> activeFacades = new Dictionary<Type, RFacade>();

        private static readonly HashSet<Type> pendingFacades = new HashSet<Type>();

        public static void Create(Type facadeConcreteType, IRAppShell? appShell = null) 
        {
            if (facadeConcreteType == null) 
            {
                throw new ArgumentNullException(nameof(facadeConcreteType), "The facadeConcreteType cannot be null.");
            }

            if (activeFacades.ContainsKey(facadeConcreteType)) 
            {
                throw new InvalidOperationException($"Could not instantiate: {facadeConcreteType}. It may already have been created.");
            }

            try 
            {
                pendingFacades.Add(facadeConcreteType);
                
                var facade = Activator.CreateInstance(facadeConcreteType) as RFacade;

                if (facade == null) 
                {
                    throw new InvalidOperationException("Failed to create an instance of the facade.");
                }

                activeFacades.Add(facadeConcreteType, facade);

                if (promoterType != null && facadeConcreteType.Equals(promoterType)) 
                {
                    promoterFacade = facade;
                }
                else if (promoterFacade == null) {
                    promoterFacade = facade;
                }

                if (AppShell == null) 
                {
                    AppShell = appShell;
                }

                facade.rCommander = new RCommander(facade);

                var models = facade.RegisterModels();
                
                foreach (RModel model in models) 
                {
                    facade.modelsDictionary.Add(model.GetType(), model);
                    model.rCommander = facade.rCommander;
                    model.Initialise();
                }

                var mediators = facade.RegisterMediators();
                
                foreach (RMediator mediator in mediators) 
                {
                    facade.mediatorsDictionary.Add(mediator.GetType(), mediator);
                    mediator.rCommander = facade.rCommander;
                }

                RCommandBase startupCommand = facade.RegisterStartupCommand();

                if (startupCommand != null) 
                {
                    facade.ExecuteCommand(startupCommand);
                }

                Log($"Facade execution completed: {facadeConcreteType?.Name}.");
            }
            catch (Exception ex) 
            {
                pendingFacades.Remove(facadeConcreteType);
                Log($"Cannot instantiate Facade instance. Error: {ex.Message}");
                throw;
            }
        }

        public static void RegisterActor(IRContract view) 
        {
            RMediator? foundMediator = null;

            foreach (var facade in activeFacades.Values) 
            {
                if (facade is null || facade.mediatorsDictionary is null)
                    continue;

                foreach (var mediator in facade.mediatorsDictionary.Values) 
                {
                    if (mediator.viewBaseType.IsAssignableFrom(view.GetType())) {
                        foundMediator = mediator;

                        // assuming aggressive re-registering can be forgiven:
                        if (mediator.viewBase == view) {
                            return;
                        }
                        break;
                    }
                }
                if (foundMediator != null)
                    break;
            }

            if (foundMediator == null) 
            {
                //Log("Mediator is null: " + view);
                return; // Windows Forms Designer bug.
            }

            foundMediator.UpdateViewBase(view);

            Log("Registered mediator + view: " + foundMediator.GetType().Name + " | " + view.GetType().Name);
        }
        public static void UnregisterActor(IRContract view) 
        {
            RMediator? foundMediator = null;

            foreach (var facade in activeFacades.Values) {
                
                if (facade is null || facade.mediatorsDictionary is null)
                    continue;

                foreach (var mediator in facade.mediatorsDictionary.Values) 
                {
                    if (mediator.viewBaseType.IsAssignableFrom(view.GetType())) 
                    {
                        foundMediator = mediator;

                        if (foundMediator.viewBase == null) 
                        {
                            return;
                        }
                    }

                    if (foundMediator != null)
                        break;

                }

                if (foundMediator != null)
                    break;
            }

            if (foundMediator == null) 
            {
                //Log("Mediator is null: " + view);
                return; // Windows Forms Designer bug.
            }

            Log("Unregistered mediator + view: " + foundMediator.GetType().Name + " | " + view.GetType().Name);
        }
        public static void Configure(Type? promoterType = null) 
        {
            if (hasBeenConfigured)
            {
                Error("RFacade.Configure(...) should only be called once at the start of the application's life.");
            }

            RFacade.promoterType = promoterType;
            hasBeenConfigured = true;
        }

        public static void Log(object? logMessage) 
        {
            if (!EnableDebugMode)
                return;

            logMessage = "[RMVC] " + logMessage;
            Debug.WriteLine($"{logMessage}");
            Console.WriteLine($"{logMessage}");
        }


        public void AbortAllCommands() 
        {
            foreach (var cancellationSource in activeTasks.Values) 
            {
                cancellationSource.Cancel();
            }
        }

        internal void ExecuteCommand(RCommandBase command) 
        {
            ExecuteCommand(command, null);
        }

        internal void ExecuteCommand(RCommandBase command, RTracker? rTracker = null, double percentCap = 100d) 
        {
            percentCap = RHelper.ClampPercent(percentCap);

            if (command is RCommand syncCommand) 
            {
                // Synchronous execution
                syncCommand.RunInternal(this);
            }
            else if (command is RCommandAsync asyncCommand) 
            {
                // Delegate async command execution
                _ = ExecuteCommandAsync(asyncCommand, rTracker, percentCap);
            }
        }

        internal async Task ExecuteCommandAsync(
            RCommandAsync asyncCommand,
            RTracker? rTracker,
            double percentCap) 
        {
            if (!asyncCommand.hasParent && !activeTasks.IsEmpty && asyncCommand.EnableAutoUpdate) {
                Error("Cannot execute more than one top-level Async Command at a time.");
                return;
            }

            var cancellationTokenSource = rTracker != null ? CancellationTokenSource.CreateLinkedTokenSource(rTracker.Token) : new CancellationTokenSource();

            rTracker ??= new RTracker(asyncCommand, this, percentCap, cancellationTokenSource.Token);

            var task = Task.Run(async () =>
            {
                try 
                {
                    await asyncCommand.RunInternalAsync(rTracker);
                }
                catch (Exception ex) 
                {
                    Log("ERROR CAUGHT in async command: " + ex.ToString());
                    asyncCommand.SetErrorInternal("RFacade Async Execution Error.");
                }
            }, cancellationTokenSource.Token);

            // Track only top-level async commands
            if (!asyncCommand.hasParent && !activeTasks.ContainsKey(task)) 
            {
                activeTasks[task] = cancellationTokenSource;
            }

            try 
            {
                await task;
            }
            finally 
            {
                if (!asyncCommand.hasParent && activeTasks.ContainsKey(task)) 
                {
                    HandleTaskComplete(asyncCommand);
                    activeTasks.TryRemove(task, out _);
                    Log($"Active tasks: {activeTasks.Count}");
                }
            }
        }

        protected abstract RCommandBase RegisterStartupCommand();
        protected abstract RMediator[] RegisterMediators();
        protected abstract RModel[] RegisterModels();

        protected static TFacade? FacadeInstance<TFacade>() where TFacade : RFacade 
        {
            if (activeFacades.TryGetValue(typeof(TFacade), out RFacade? facade)) 
            {
                return facade as TFacade;
            }
            else 
                return null;
        }

        protected TMediator? Mediator<TMediator>() where TMediator : RMediator 
        {
            if (mediatorsDictionary.TryGetValue(typeof(TMediator), out RMediator? mediator)) 
            {
                return mediator as TMediator;
            }
            else 
                return null;
        }

        protected TModel? Model<TModel>() where TModel : RModel 
        {
            if (modelsDictionary.TryGetValue(typeof(TModel), out RModel? model)) 
            {
                return model as TModel;
            }
            else
                return null;
        }

        protected static RFacade? GetPromoter() 
        {
            return promoterFacade;
        }

        private void HandleTaskComplete(RCommandAsync command) 
        {
            command.HandleThreadExit();

            if (!command.hasParent && command.EnableAutoUpdate) 
            {
                command.rTracker?.facade.HandleProgressComplete();
            }
        }

        protected RFacade() {
            if (!pendingFacades.Contains(GetType()))
            {
                Fatal("Do not instantiate a Facade directly. Use the static Initialise() method.");
            }
            pendingFacades.Remove(GetType());
        }


        private void HandleProgressComplete() 
        {
            if (promoterFacade == null) 
                return;
            
            RCommandBase? cmd = promoterFacade.RegisterProgressCompleteCommand();
            
            if (cmd != null)
                ExecuteCommand(cmd);
        }

        internal void HandleProgressChange(RProgress[] progress) 
        {    
            if ((DateTime.Now - lastProgressUpdateTime).TotalMilliseconds < 100)
                return;
            else
                lastProgressUpdateTime = DateTime.Now;

            if (promoterFacade == null) 
                return;
            
            RCommandBase? cmd = promoterFacade.RegisterProgressUpdateCommand(progress);
            
            if (cmd != null)
                ExecuteCommand(cmd);
        }

        virtual protected RCommandBase? RegisterProgressUpdateCommand(RProgress[] progress) 
        {
            return null;
        }

        virtual protected RCommandBase? RegisterProgressCompleteCommand() 
        {
            return null;
        }

        private static void Fatal(string message) 
        {       
            throw new Exception(message);
        }

        private static void Error(string message) 
        {
            Log("RMVC - ERROR: " + message);
        }

    }
}
