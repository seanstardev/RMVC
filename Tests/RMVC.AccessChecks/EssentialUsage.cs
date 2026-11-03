using RMVC;
namespace ApplicationConsumer;
public sealed class Bridge : RBridge<Child>
{
    public void Request() => Execute(new WorkRoot());
}
public sealed class Sender : RModel
{
    public void Request() => Execute(new WorkRoot());
}
public sealed class Presenter : RMediator<IView>
{
    protected override void ActorAttached(IView actor) { }
    protected override void ActorDetached(IView actor) { }
    public void Request() => Execute(new WorkRoot());
}
public sealed class Gateway : FacadeBase
{
    public void Request() => Execute(new WorkRoot());
    protected override RBridge[] RegisterBridges() => [new Bridge()];
    protected override RCommandBase? CreateProgressUpdateCommand(string root, RProgress[] progress) => new WorkRoot();
    protected override RCommandBase? CreateProgressCompleteCommand(string root) => new WorkRoot();
    public void PublicHostOperationsRemainCallable(IRShell shell)
    {
        RFacade.EnableDebugMode = true;
        RFacade.Create<Child>(shell);
        RFacade.Child<Child>();
        RFacade.RegisterActor<Child, IView>(null!);
        RFacade.UnregisterActor<Child, IView>(null!);
        RFacade.AbortCommands<Child>();
        _ = RFacade.Shutdown(1500);
        RFacade.AbortAllCommands();
    }
}
public sealed class Work : RCommand<RVoid>
{
    protected override Task<RVoid> Run()
    {
        _ = Model<Model>();
        _ = ModelOrNull<Model>();
        _ = ModelByContract<IModel>();
        _ = Mediator<Mediator>();
        _ = MediatorOrNull<Mediator>();
        _ = MediatorByContract<IModel>();
        _ = Shell;
        _ = FacadeChild<Child>();
        _ = FacadeParentByContract<IParent>();
        _ = BridgeChild<Bridge>();
        return Complete();
    }
}
