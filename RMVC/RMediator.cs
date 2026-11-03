using System;
using System.Collections.Generic;
namespace RMVC
{
    public abstract class RMediator : RCommandExecutorBase
    {
        private readonly object actorGate = new object();
        private readonly Queue<Action<IRContract>> pendingActorCalls = new Queue<Action<IRContract>>();
        private bool actorAttached;
        private bool drainingActorCalls;
        internal IRContract? ActorBase { get; private set; }
        internal Type ActorContractType { get; }

        internal RMediator(Type actorContractType) : base()
        {
            ActorContractType = actorContractType;
        }

        // Retains references: use immutable data. Actors own UI dispatch.
        internal void EnqueueActorAction(Action<IRContract> action)
        {
            lock (actorGate)
            {
                pendingActorCalls.Enqueue(action);

                if (!actorAttached || drainingActorCalls) return;

                drainingActorCalls = true;
            }

            DrainActorCalls();
        }

        // Applications coordinate registration and unregistration.
        internal void UpdateActorBase(IRContract actor)
        {
            if (!ActorContractType.IsInstanceOfType(actor))
                throw new ArgumentException("Actor does not implement the mediator contract.", nameof(actor));

            RemoveActorBase();

            lock (actorGate) 
            { 
                ActorBase = actor; 
                actorAttached = false; 
            }

            try
            {
                NotifyActorAttached(actor);
            }
            catch
            {
                try
                {
                    RemoveActorBase();
                }
                catch (Exception ex)
                {
                    RFacade.Log(
                        $"ACTOR CLEANUP ERROR: mediator={GetType().FullName}; {ex}");
                }

                throw;
            }

            lock (actorGate)
            {
                actorAttached = true;

                if (drainingActorCalls || pendingActorCalls.Count == 0) 
                    return;
                
                drainingActorCalls = true;
            }

            DrainActorCalls();
        }

        internal void RemoveActorBase()
        {
            IRContract actor;

            lock (actorGate)
            {
                if (ActorBase == null) return;
                actor = ActorBase;

                actorAttached = false;

                pendingActorCalls.Clear();
            }
            try
            {
                NotifyActorDetached(actor);
            }
            finally
            {
                lock (actorGate) 
                { 
                    ActorBase = null; 
                    pendingActorCalls.Clear(); 
                }
            }
        }

        private void DrainActorCalls()
        {
            try
            {
                while (true)
                {
                    Action<IRContract> action;

                    IRContract actor;

                    lock (actorGate)
                    {
                        if (!actorAttached || ActorBase == null || pendingActorCalls.Count == 0)
                        {
                            drainingActorCalls = false;
                            return;
                        }

                        actor = ActorBase;

                        action = pendingActorCalls.Dequeue();
                    }

                    action(actor);
                }
            }
            catch
            {
                lock (actorGate)
                {
                    pendingActorCalls.Clear();
                    drainingActorCalls = false;
                }

                throw;
            }
        }
        internal abstract void NotifyActorAttached(IRContract actor);
        internal abstract void NotifyActorDetached(IRContract actor);
    }

    public abstract class RMediator<TActor> : RMediator
        where TActor : class, IRContract
    {
        protected RMediator() : base(typeof(TActor))
        {

        }

        /// <summary>The framework-owned current attachment; application mediators must not retain another copy.</summary>
        protected TActor? Actor => (TActor?)ActorBase;

        internal sealed override void NotifyActorAttached(IRContract actor)
            => ActorAttached((TActor)actor);

        internal sealed override void NotifyActorDetached(IRContract actor)
            => ActorDetached((TActor)actor);

        /// <summary>Receives the non-null attachment for subscription; do not store it.</summary>
        protected abstract void ActorAttached(TActor actor);

        /// <summary>Receives the departing attachment for unsubscription; do not store it.</summary>
        protected abstract void ActorDetached(TActor actor);

        protected void WithActor(Action<TActor> action)
            => EnqueueActorAction(actor => action((TActor)actor));
    }
}
