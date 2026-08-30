namespace RMVC
{
    public abstract class RCommandBase : RActor
    {
        protected internal RFacade? facade;

        abstract internal void ExecuteUntypedInternal(
            RFacade facade,
            RTracker? rTracker = null,
            double percentCap = 100d);

        protected void ExecuteCommand(RCommand command)
        {
            facade?.ExecuteCommand(command);
        }

        protected TResult ExecuteCommand<TResult>(RCommand<TResult> command)
        {
            return facade != null
                ? facade.ExecuteCommand(command)
                : default!;
        }
    }
}