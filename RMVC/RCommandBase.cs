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

        protected TModel? Model<TModel>()
            where TModel : class, IRModel
        {
            return facade?.ResolveModel<TModel>();
        }

        protected TMediator? Mediator<TMediator>()
            where TMediator : RMediator
        {
            return facade?.ResolveMediator<TMediator>();
        }
    }
}