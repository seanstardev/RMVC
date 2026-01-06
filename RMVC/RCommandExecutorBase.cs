namespace RMVC 
{
    public abstract class RCommandExecutorBase : RActor 
    {
        internal RCommander? rCommander;

        protected void ExecuteCommand(RCommandBase command) 
        {
            rCommander?.Facade?.ExecuteCommand(command);
        }
    }
}
