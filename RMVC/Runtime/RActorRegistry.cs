using System;
using System.Collections.Generic;

namespace RMVC
{
    /// <summary>Owns one facade's model and mediator registrations and actor lookup rules.</summary>
    internal sealed class RActorRegistry
    {
        private readonly RFacade owner;
        private readonly Dictionary<Type, IRModel> modelsDictionary =
            new Dictionary<Type, IRModel>();
        private readonly Dictionary<Type, RMediator> mediatorsDictionary =
            new Dictionary<Type, RMediator>();
        private readonly Dictionary<Type, RMediator> contractMediatorsDictionary =
            new Dictionary<Type, RMediator>();

        internal RActorRegistry(RFacade owner)
        {
            this.owner = owner;
        }

        internal void RegisterModels(IRModel[] models)
        {
            foreach (IRModel model in models)
            {
                var modelType = model.GetType();

                if (modelsDictionary.ContainsKey(modelType))
                {
                    throw new InvalidOperationException(
                        $"Facade '{owner.GetType().FullName}' registers model " +
                        $"'{modelType.FullName}' more than once.");
                }

                modelsDictionary.Add(modelType, model);

                if (model is RModel rModel)
                    rModel.Facade = owner;
            }

        }

        internal void RegisterMediators(RMediator[] mediators)
        {
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

                mediatorsDictionary.Add(mediatorType, mediator);
                contractMediatorsDictionary.Add(contractType, mediator);

                mediator.Facade = owner;
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

            RFacade.Log($"ACTOR REGISTERED: mediator={mediator.GetType().FullName}; contract={mediator.ActorContractType.FullName}; actor={actor.GetType().FullName}.");
        }

        internal static void DetachActor(RMediator? mediator, IRContract actor)
        {
            if (mediator == null || !ReferenceEquals(mediator.ActorBase, actor))
                return;

            mediator.RemoveActorBase();

            RFacade.Log($"ACTOR UNREGISTERED: mediator={mediator.GetType().FullName}; contract={mediator.ActorContractType.FullName}; actor={actor.GetType().FullName}.");
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

        internal TContract? ResolveModelByContract<TContract>()
            where TContract : class
        {
            return ResolveUniqueByContract<TContract>(
                modelsDictionary.Values,
                "model");
        }

        internal TContract? ResolveMediatorByContract<TContract>()
            where TContract : class
        {
            return ResolveUniqueByContract<TContract>(
                mediatorsDictionary.Values,
                "mediator");
        }

        private TContract? ResolveUniqueByContract<TContract>(
            IEnumerable<object> actors,
            string category)
            where TContract : class
        {
            TContract? match = null;

            foreach (object actor in actors)
            {
                if (!(actor is TContract candidate))
                    continue;

                if (match != null)
                {
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
