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
                Initialised();
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
            lock (actorGate)
            {
                if (ActorBase == null) return;

                actorAttached = false;

                pendingActorCalls.Clear();
            }
            try
            {
                Disposing();
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
        protected abstract void Initialised();
        protected abstract void Disposing();
    }

    public abstract class RMediator<TActor> : RMediator
        where TActor : class, IRContract
    {
        protected RMediator() : base(typeof(TActor))
        {

        }

        protected TActor? actor => (TActor?)ActorBase;

        protected void WithActor(Action<TActor> action)
            => EnqueueActorAction(actor => action((TActor)actor));
    }
}
