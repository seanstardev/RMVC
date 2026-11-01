namespace RMVC
{
    public abstract class RCommandExecutorBase
    {
        public string Name => GetType().Name;

        internal RCommandExecutorBase() { }

        internal RFacade? Facade { get; set; }

        protected TContract? GetFacadeParentByContract<TContract>()
            where TContract : class, IRFacade
            => Facade?.ResolveFacadeParentByContract<TContract>();

        protected TFacade? GetFacadeChild<TFacade>()
            where TFacade : RFacade
            => Facade?.ResolveFacadeChild<TFacade>();

        protected void Execute(RCommandBase command)
            => Facade?.Execute(command, GetType().FullName);

        protected void ExecuteSequentially(
            params RCommandBase[] commands)
        {
            Facade?.ExecuteSequentially(
                commands,
                GetType().FullName);
        }
    }
}
