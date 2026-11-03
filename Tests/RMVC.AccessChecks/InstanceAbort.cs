using RMVC;
namespace ApplicationConsumer;
public sealed class InstanceAbort : FacadeBase
{
    public void Incorrect(RFacade facade) => facade.AbortAllCommands();
}
