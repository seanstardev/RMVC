using RMVC;
namespace ApplicationConsumer;
public sealed class FacadeProbe : FacadeBase
{
    public object A() => Model<Model>();
    public object? B() => ModelOrNull<Model>();
    public object? C() => ModelByContract<IModel>();
    public object D() => Mediator<Mediator>();
    public object? E() => MediatorOrNull<Mediator>();
    public object? F() => MediatorByContract<IModel>();
    public object? G() => FacadeChild<Child>();
    public object? H() => FacadeParentByContract<IParent>();
    public object I() => AppShell;
}
public sealed class ModelProbe : RModel
{
    public object? A() => FacadeChild<Child>();
    public object? B() => FacadeParentByContract<IParent>();
}
public sealed class MediatorProbe : RMediator<IView>
{
    protected override void ActorAttached(IView actor) { }
    protected override void ActorDetached(IView actor) { }
    public object? A() => FacadeChild<Child>();
    public object? B() => FacadeParentByContract<IParent>();
}
public sealed class BridgeProbe : RBridge<Child>
{
    public object? A() => FacadeChild<Child>();
    public object? B() => FacadeParentByContract<IParent>();
}
