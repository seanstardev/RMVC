namespace RMVC
{
    public abstract class RCommandExecutorBase
    {
        public string Name => GetType().Name;

        internal RCommandExecutorBase() { }

        internal RFacade? Facade { get; set; }

        protected void Execute(RCommandBase command)
            => Facade?.Execute(command, GetType().FullName);
    }
}
