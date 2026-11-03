using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;

namespace RMVC
{
    /// <summary>Owns one facade's model and mediator registrations and actor lookup rules.</summary>
    internal sealed class RActorRegistry
    {
        private static readonly object ownershipGate = new object();
        private static readonly Dictionary<object, RFacade> owners =
            new Dictionary<object, RFacade>(new ReferenceComparer());

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            bool IEqualityComparer<object>.Equals(object? left, object? right)
                => ReferenceEquals(left, right);

            public int GetHashCode(object instance)
                => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(instance);
        }

        private readonly RFacade owner;
        private int modelsRegistrationStarted;
        private int mediatorsRegistrationStarted;
        private readonly Dictionary<Type, IRModel> modelsDictionary =
            new Dictionary<Type, IRModel>();

        private readonly List<IRModel> modelsRegistrationOrder = new List<IRModel>();
        private readonly object cleanupGate = new object();
        private Task<IReadOnlyList<Exception>>? modelCleanup;
        private readonly Dictionary<Type, RMediator> mediatorsDictionary =
            new Dictionary<Type, RMediator>();

        private readonly Dictionary<Type, RMediator> contractMediatorsDictionary =
            new Dictionary<Type, RMediator>();

        internal RActorRegistry(RFacade owner)
        {
            this.owner = owner;
        }

        internal void RegisterModels(Func<IRModel[]> createModels)
        {
            BeginRegistration(ref modelsRegistrationStarted, "RegisterModels");
            IRModel[] models = createModels();
            // Retain every eligible instance handed to RMVC, even if validation later
            // rejects the array. Never take another composition's model for cleanup.
            if (models != null)
            {
                lock (ownershipGate)
                {
                    foreach (var model in models)
                    {
                        if (model == null || owners.TryGetValue(model, out var existingOwner)
                            && !ReferenceEquals(existingOwner, owner))
                            continue;

                        bool retained = false;

                        foreach (var existing in modelsRegistrationOrder)
                            if (ReferenceEquals(existing, model)) 
                            { 
                                retained = true; 
                                break; 
                            }

                        if (retained) continue;

                        ClaimOwnership(model);
                        
                        modelsRegistrationOrder.Add(model);
                    }
                }
            }

            ValidateRegistrations(models!, "RegisterModels");

            foreach (IRModel model in models!)
            {
                var modelType = model.GetType();

                if (modelsDictionary.ContainsKey(modelType))
                {
                    throw new InvalidOperationException(
                        $"Facade '{owner.GetType().FullName}' registers model " +
                        $"'{modelType.FullName}' more than once.");
                }

                ClaimOwnership(model);

                modelsDictionary.Add(modelType, model);
            }

        }

        // This registry is complete before initialisation begins. Retain explicit order:
        // dictionary enumeration order is not a portable registration-order contract.
        internal Task<IReadOnlyList<Exception>> CleanupModels()
        {
            TaskCompletionSource<IReadOnlyList<Exception>> completion;
            lock (cleanupGate)
            {
                if (modelCleanup != null) 
                    return modelCleanup;
                
                completion = new TaskCompletionSource<IReadOnlyList<Exception>>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                
                modelCleanup = completion.Task;
            }

            // Never invoke application cleanup while holding a framework lock.
            _ = CleanupModelsAsync(completion);

            return completion.Task;
        }

        private async Task CleanupModelsAsync(
            TaskCompletionSource<IReadOnlyList<Exception>> completion)
        {
            var failures = new List<Exception>();

            for (int index = modelsRegistrationOrder.Count - 1; index >= 0; index--)
            {
                IRModel model = modelsRegistrationOrder[index];

                try
                {
                    Task cleanup = model.Cleanup() ?? throw new InvalidOperationException(
                        "Cleanup() returned null. Return Task.CompletedTask when there is nothing to release.");

                    await cleanup.ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    failures.Add(new InvalidOperationException(
                        $"Cleanup failed for model '{model.GetType().FullName}' " +
                        $"in facade '{owner.GetType().FullName}': {error.Message}", error));
                }
            }
            completion.SetResult(failures.AsReadOnly());
        }

        internal void RegisterMediators(Func<RMediator[]> createMediators)
        {
            BeginRegistration(ref mediatorsRegistrationStarted, "RegisterMediators");
            RMediator[] mediators = createMediators();
            ValidateRegistrations(mediators, "RegisterMediators");

            foreach (RMediator mediator in mediators)
            {
                var mediatorType = mediator.GetType();

                if (mediatorsDictionary.ContainsKey(mediatorType))
                {
                    throw new InvalidOperationException(
                        $"Facade '{owner.GetType().FullName}' registers mediator " +
                        $"'{mediatorType.FullName}' more than once.");
                }

                var contractType = mediator.ActorContractType;
                ValidateContractType(contractType);

                if (contractMediatorsDictionary.TryGetValue(
                    contractType, 
                    out var existingMediator))
                {
                    throw new InvalidOperationException(
                        $"Facade '{owner.GetType().FullName}' registers contract '{contractType.FullName}' " +
                        $"with both '{existingMediator.GetType().FullName}' and '{mediator.GetType().FullName}'.");
                }

                ClaimOwnership(mediator);
                mediatorsDictionary.Add(mediatorType, mediator);
                contractMediatorsDictionary.Add(contractType, mediator);
            }

        }

        // Claim before invoking application factories, including empty or failed attempts.
        // Do not run application code under a registry lock.
        private void BeginRegistration(ref int started, string method)
        {
            if (Interlocked.Exchange(ref started, 1) != 0)
                throw new InvalidOperationException(
                    $"Facade '{owner.GetType().FullName}': {method}() registration has already been attempted. Registration is permitted once.");
        }

        private void ClaimOwnership(object actor)
        {
            lock (ownershipGate)
            {
                if (owners.TryGetValue(actor, out var existingOwner)
                    && !ReferenceEquals(existingOwner, owner))
                {
                    throw new InvalidOperationException(
                        $"Actor '{actor.GetType().FullName}' is already registered " +
                        $"with facade '{existingOwner.GetType().FullName}' and cannot " +
                        $"also belong to facade '{owner.GetType().FullName}'.");
                }

                owners[actor] = owner;

                if (actor is RCommandExecutorBase executor)
                    executor.Facade = owner;
            }
        }

        // Release only this facade's claims when composition fails before publication.
        internal void ReleaseOwnership()
        {
            lock (ownershipGate)
            {
                var instances = new List<object>();

                foreach (var entry in owners)
                {
                    if (ReferenceEquals(entry.Value, owner))
                        instances.Add(entry.Key);
                }

                foreach (object instance in instances)
                {
                    owners.Remove(instance);

                    if (instance is RCommandExecutorBase executor
                        && ReferenceEquals(executor.Facade, owner))
                    {
                        executor.Facade = null;
                    }
                }
            }
        }

        private void ValidateRegistrations<T>(
            T[] registrations,
            string registrationMethod)
            where T : class
        {
            if (registrations == null)
            {
                throw new InvalidOperationException(
                    $"Facade '{owner.GetType().FullName}': " +
                    $"{registrationMethod}() returned null. " +
                    "Return an empty array when there are no registrations.");
            }

            for (int index = 0; index < registrations.Length; index++)
            {
                if (registrations[index] == null)
                {
                    throw new InvalidOperationException(
                        $"Facade '{owner.GetType().FullName}': " +
                        $"{registrationMethod}() returned a null entry " +
                        $"at index {index}.");
                }
            }
        }

        internal RMediator? FindContractMediator(Type contractType)
        {
            return contractMediatorsDictionary.TryGetValue(contractType, out var mediator)
                ? mediator
                : null;
        }

        internal static void ValidateActor<TActor>(IRContract actor)
            where TActor : class, IRContract
        {
            if (actor == null) throw new ArgumentNullException(nameof(actor));
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

        internal static void ValidateActorRegistration<TActor>(IRContract actor)
            where TActor : class, IRContract
        {
            ValidateActor<TActor>(actor);

            var contracts = new List<Type>();

            foreach (var candidate in actor.GetType().GetInterfaces())
            {
                if (candidate != typeof(IRContract) && typeof(IRContract).IsAssignableFrom(candidate))
                    contracts.Add(candidate);
            }

            if (contracts.Count > 1)
            {
                contracts.Sort((left, right) 
                    => string.CompareOrdinal(left.FullName, right.FullName));

                var names = new List<string>();
                
                foreach (var contract in contracts) 
                    names.Add(contract.FullName ?? contract.Name);
                
                throw new InvalidOperationException(
                    $"Actor '{actor.GetType().FullName}' implements more than one IRContract: " +
                    $"{string.Join(", ", names)}. An attached object must implement exactly one actor contract.");
            }
        }

        internal static void ValidateContractType(Type contractType)
        {
            if (!contractType.IsInterface)
            {
                throw new ArgumentException(
                    $"Actor contract '{contractType.FullName}' must be an interface extending IRContract. " +
                    "Specify the contract interface explicitly, for example RegisterActor<IMyView>(view) " +
                    "or UnregisterActor<IMyView>(view).");
            }
        }

        internal static void AttachActor(RMediator? mediator, IRContract actor)
        {
            if (mediator == null || ReferenceEquals(mediator.ActorBase, actor))
                return;

            mediator.UpdateActorBase(actor);

            RFacade.Log(
                $"ACTOR REGISTERED: mediator={mediator.GetType().FullName}; contract={mediator.ActorContractType.FullName}; actor={actor.GetType().FullName}.");
        }

        internal static void DetachActor(RMediator? mediator, IRContract actor)
        {
            if (mediator == null || !ReferenceEquals(mediator.ActorBase, actor))
                return;

            mediator.RemoveActorBase();

            RFacade.Log($"ACTOR UNREGISTERED: mediator={mediator.GetType().FullName}; contract={mediator.ActorContractType.FullName}; actor={actor.GetType().FullName}.");
        }

        internal TModel ResolveModel<TModel>()
            where TModel : class, IRModel
            => ResolveUnique<TModel>(modelsDictionary.Values, "model", required: true)
                ?? throw new InvalidOperationException(
                    $"Facade '{owner.GetType().FullName}' has no registered " +
                    $"model '{typeof(TModel).FullName}'.");

        internal TModel? ResolveModelOrNull<TModel>()
            where TModel : class, IRModel
            => ResolveUnique<TModel>(modelsDictionary.Values, "model");

        internal TMediator ResolveMediator<TMediator>()
            where TMediator : RMediator
            => ResolveUnique<TMediator>(mediatorsDictionary.Values, "mediator", required: true)
                ?? throw new InvalidOperationException(
                    $"Facade '{owner.GetType().FullName}' has no registered " +
                    $"mediator '{typeof(TMediator).FullName}'.");

        internal TMediator? ResolveMediatorOrNull<TMediator>()
            where TMediator : RMediator
            => ResolveUnique<TMediator>(mediatorsDictionary.Values, "mediator");

        internal TContract? ResolveModelByContract<TContract>()
            where TContract : class
        {
            ValidateLookupContract<TContract>();

            return ResolveUnique<TContract>(
                modelsDictionary.Values,
                "model");
        }

        internal TContract? ResolveMediatorByContract<TContract>()
            where TContract : class
        {
            ValidateLookupContract<TContract>();

            return ResolveUnique<TContract>(
                mediatorsDictionary.Values,
                "mediator");
        }

        internal static void ValidateLookupContract<TContract>()
            where TContract : class
        {
            if (!typeof(TContract).IsInterface)
            {
                throw new ArgumentException(
                    $"Lookup contract '{typeof(TContract).FullName}' must be an interface. " +
                    "Use Model/ModelOrNull or Mediator/MediatorOrNull for class lookups.");
            }
        }

        private TContract? ResolveUnique<TContract>(
            IEnumerable<object> actors,
            string category,
            bool required = false)
            where TContract : class
        {
            TContract? match = null;

            foreach (object actor in actors)
            {
                if (!(actor is TContract candidate))
                    continue;

                if (match != null)
                {
                    if (required)
                    {
                        throw new InvalidOperationException(
                            $"Facade '{owner.GetType().FullName}' has multiple registered " +
                            $"{category}s matching '{typeof(TContract).FullName}': " +
                            $"'{match.GetType().FullName}', '{candidate.GetType().FullName}'.");
                    }

                    if (RFacade.EnableDebugMode)
                    {
                        RFacade.Log(
                            $"AMBIGUOUS ACTOR LOOKUP: facade={owner.GetType().FullName}; " +
                            $"category={category}; contract={typeof(TContract).FullName}; " +
                            $"matches={match.GetType().FullName}, {candidate.GetType().FullName}.");
                    }

                    return null;
                }

                match = candidate;
            }

            return match;
        }
    }
}
