using RMVC;

namespace ApplicationConsumer;

public sealed class ScopedAbort
{
    public void Incorrect()
    {
        RFacade.AbortCommands<Child>();
        RFacade.AbortRootCommands<Child, WorkRoot>();
    }
}
