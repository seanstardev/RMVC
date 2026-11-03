using RMVC;

namespace RMVC.ContractLookupChecks;

internal static class ActorLifecycleChecks
{
    private static void Check(bool condition, string message) => Program.Check(condition, message);
    private static bool Throws(Action action, params string[] words)
    {
        try { action(); }
        catch (Exception error) { return words.All(error.Message.Contains); }
        return false;
    }

    internal static async Task Run()
    {
        RFacade.Create<Host>(new Shell());
        var mediator = Host.Instance.Presenter;
        var first = new AttachedObject();
        mediator.Present();
        RFacade.RegisterActor<Host, IObject>(first);
        Check(mediator.Attaches == 1 && mediator.LastArgumentMatches && first.Presentations == 1,
            "non-null attachment argument matches getter-only Actor and releases queued calls");
        Check(!typeof(RMediator<IObject>).GetProperty("Actor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.CanWrite,
            "application Actor property has no setter");
        RFacade.RegisterActor<Host, IObject>(first);
        Check(mediator.Attaches == 1 && mediator.Detaches == 0, "repeat registration remains idempotent");
        Check(Throws(() => RFacade.RegisterActor<Host, IObject>(new MultipleObject()),
            "more than one IRContract", typeof(IObject).FullName!, typeof(ISecond).FullName!),
            "multiple contracts are rejected before mediator attachment changes");
        Check(Throws(() => RFacade.RegisterActor<Host, ISecond>(new MultipleObject()), "more than one IRContract"),
            "multiple contracts are rejected even without a matching mediator");
        Check(Throws(() => RFacade.RegisterActor<Host, IDerived>(new DerivedContractObject()), "more than one IRContract"),
            "inherited additional actor contracts are rejected too");
        RFacade.UnregisterActor<Host, IObject>(new MultipleObject());
        Check(mediator.IsCurrent(first), "unregistering an unrelated instance does not trigger registration-only validation");
        Check(mediator.Attaches == 1 && mediator.Detaches == 0 && mediator.IsCurrent(first),
            "rejected registrations preserve the existing attachment");
        Check(Throws(() => RFacade.RegisterActor<Host, IObject>(null!), "actor"), "null actor registration fails clearly");

        first.Fire();
        Check(mediator.Requests == 1, "typed attach hook subscribes to events");
        var second = new DerivedObject();
        RFacade.RegisterActor<Host, IObject>(second);
        first.Fire(); second.Fire();
        Check(mediator.Attaches == 2 && mediator.Detaches == 1 && mediator.Requests == 2,
            "replacement detaches the previous instance and attaches the new instance");
        Check(first.Disposals == 0, "replacement does not dispose the externally owned object");
        RFacade.UnregisterActor<Host, IObject>(first);
        Check(mediator.IsCurrent(second), "stale unregistration does not detach the current instance");
        RFacade.UnregisterActor<Host, IObject>(second);
        Check(mediator.Detaches == 2 && mediator.IsCurrent(null) && second.Disposals == 0,
            "ordinary unregistration detaches without disposing the object");
        second.Fire();
        Check(mediator.Requests == 2, "detach hook receives the old object and removes subscriptions");

        var broken = new AttachedObject { FailAttach = true };
        Check(Throws(() => RFacade.RegisterActor<Host, IObject>(broken), "attach failed"), "attachment exceptions propagate");
        broken.Fire();
        Check(mediator.IsCurrent(null) && mediator.Detaches == 3 && mediator.Requests == 2 && broken.Disposals == 0,
            "failed attachment unsubscribes and clears the reference without disposing the object");
        mediator.Present();
        var last = new AttachedObject { FailDetach = true };
        RFacade.RegisterActor<Host, IObject>(last);
        Check(last.Presentations == 1, "a later attachment can receive queued work after failed attachment");
        Check(Throws(() => RFacade.UnregisterActor<Host, IObject>(last), "detach failed"), "detach exceptions propagate");
        Check(mediator.IsCurrent(null), "detach failure still clears the framework reference");
        last.Fire();
        Check(mediator.Requests == 2, "detach hook can unsubscribe before reporting its own error");
        var restored = new AttachedObject();
        RFacade.RegisterActor<Host, IObject>(restored);
        Check(mediator.IsCurrent(restored), "mediator remains reusable after detach failure");
        RFacade.UnregisterActor<Host, IObject>(restored);

        var plain = new PlainModel();
        await ((IRModel)plain).Cleanup();
        Check(plain.Cleanups == 1, "plain IRModel implementations supply cleanup without RModel inheritance");
        await new DefaultModel().Cleanup();
        Check(true, "RModel supplies a completed default cleanup without requiring a base call");
        var model = new AwaitedModel();
        var cleanup = model.Cleanup();
        Check(!cleanup.IsCompleted && !model.Finished, "model cleanup can represent asynchronous work");
        model.Release.SetResult(); await cleanup;
        Check(model.Finished, "model cleanup is awaitable to completion");
        Check(typeof(IRContract).GetMethod("Cleanup") == null,
            "IRContract imposes no cleanup obligation on attached objects");
        Check(typeof(Presenter).GetMethod("Cleanup") == null,
            "mediators expose no public cleanup forwarding method");
        var failing = new FailingModel();
        bool failed = false;
        try { await failing.Cleanup(); } catch (InvalidOperationException error) { failed = error.Message == "cleanup failed"; }
        Check(failed, "cleanup failures are not hidden by the model base");
    }

    public interface IObject : IRContract { event Action? Request; }
    public interface ISecond : IRContract { }
    public interface IDerived : IObject { }
    public interface IOrdinaryCapability { }
    public class AttachedObject : IObject, IOrdinaryCapability, IDisposable
    {
        public event Action? Request;
        internal bool FailAttach, FailDetach;
        internal int Presentations, Disposals;
        internal void Fire() => Request?.Invoke();
        public void Dispose() => Disposals++;
    }
    public sealed class DerivedObject : AttachedObject { }
    public sealed class MultipleObject : AttachedObject, ISecond { }
    public sealed class DerivedContractObject : AttachedObject, IDerived { }
    private sealed class PlainModel : IRModel
    {
        internal int Cleanups;
        public Task Cleanup() { Cleanups++; return Task.CompletedTask; }
    }
    private sealed class DefaultModel : RModel { }
    private sealed class FailingModel : RModel
    {
        public override Task Cleanup() => Task.FromException(new InvalidOperationException("cleanup failed"));
    }
    private sealed class AwaitedModel : RModel
    {
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Finished;
        public override async Task Cleanup() { await Release.Task; Finished = true; }
    }
    public sealed class Host : RFacade
    {
        internal static Host Instance = null!;
        internal readonly Presenter Presenter = new();
        public Host() => Instance = this;
        protected override IRModel[] RegisterModels() => [];
        protected override RMediator[] RegisterMediators() => [Presenter];
        protected override RCommandBase CreateInitialiseCommand(IRInitialisation? initialisation) => new Initialise();
    }
    private sealed class Initialise : RCommand<RVoid>
    {
        protected override Task<RVoid> Run() => Complete();
    }
    public sealed class Presenter : RMediator<IObject>
    {
        internal int Attaches, Detaches, Requests;
        internal bool LastArgumentMatches;
        internal bool IsCurrent(IObject? expected) => ReferenceEquals(Actor, expected);
        internal void Present() => WithActor(actor => ((AttachedObject)actor).Presentations++);
        protected override void ActorAttached(IObject actor)
        {
            Attaches++; LastArgumentMatches = ReferenceEquals(Actor, actor);
            actor.Request += OnRequest;
            if (((AttachedObject)actor).FailAttach) throw new InvalidOperationException("attach failed");
        }
        protected override void ActorDetached(IObject actor)
        {
            Detaches++; actor.Request -= OnRequest;
            if (((AttachedObject)actor).FailDetach) throw new InvalidOperationException("detach failed");
        }
        private void OnRequest() => Requests++;
    }
}
