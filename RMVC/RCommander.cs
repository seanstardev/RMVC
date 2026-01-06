namespace RMVC {
    internal class RCommander 
    {
        internal RFacade? Facade;
        internal RCommander(RFacade facade) 
        {
            Facade = facade;
        }
        public void ExecuteCommand(RCommandBase command) 
        {
            Facade?.ExecuteCommand(command);
        }
    }
}
