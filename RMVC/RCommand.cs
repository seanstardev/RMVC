namespace RMVC
{
    public abstract class RCommand : RCommandBase
    {
        protected abstract void Run();

        internal void RunInternal(RFacade facade)
        {
            this.facade = facade;
            Run();
        }

        internal override void ExecuteUntypedInternal(
            RFacade facade,
            RTracker? rTracker = null,
            double percentCap = 100d)
        {
            // Synchronous execution
            RunInternal(facade);
        }
    }

    public abstract class RCommand<TResult> : RCommandBase
    {
        protected abstract TResult Run();

        internal TResult RunInternal(RFacade facade)
        {
            this.facade = facade;
            return Run();
        }

        internal override void ExecuteUntypedInternal(
            RFacade facade,
            RTracker? rTracker = null,
            double percentCap = 100d)
        {
            // Synchronous execution
            RunInternal(facade);
        }
    }
}