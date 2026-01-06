namespace RMVC 
{
    public abstract class RCommand : RCommandBase 
    {
        protected internal RFacade? facade;
        protected abstract void Run();

        internal void RunInternal(RFacade facade) 
        {
            this.facade = facade;
            Run();
        }
        internal override void ExecuteCommandInternal(RCommand command) 
        {
            facade?.ExecuteCommand(command);
        }
    }
}
