namespace RMVC {
    public abstract class RCommandBase : RActor 
    {
        abstract internal void ExecuteCommandInternal(RCommand command);

        protected void ExecuteCommand(RCommand command) 
        {
            ExecuteCommandInternal(command);
        }
    }
}
