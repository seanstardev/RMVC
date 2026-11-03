using RMVC;
namespace ApplicationConsumer;
public interface IParent : IRFacade { }
public interface IModel { }
public interface IView : IRContract { }
public sealed class Model : RModel, IModel { }
public sealed class Mediator : RMediator<IView>
{
    protected override void ActorAttached(IView actor) { }
    protected override void ActorDetached(IView actor) { }
}
public sealed class Child : FacadeBase { }
public abstract class FacadeBase : RFacade
{
    protected override IRModel[] RegisterModels() => [new Model()];
    protected override RMediator[] RegisterMediators() => [new Mediator()];
    protected override RCommandBase CreateInitialiseCommand(IRInitialisation? payload) => new Init();
}
public sealed class Init : RCommand<RVoid>
{
    protected override Task<RVoid> Run() => Complete();
}

public sealed class WorkRoot : RCommand<RVoid>
{
    protected override Task<RVoid> Run() => Complete();
}
