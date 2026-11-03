using RMVC;
namespace ApplicationConsumer;
public sealed class Probe : FacadeBase
{
    public object A() => ResolveModel<Model>();
    public object? B() => ResolveMediator<Mediator>();
    public object? C() => ResolveBridgeChild<BridgeProbe>();
}
public sealed class BridgeProbe : RBridge<Child> { }
