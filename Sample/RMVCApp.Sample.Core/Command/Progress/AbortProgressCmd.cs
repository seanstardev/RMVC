using RMVC;

namespace RMVCApp.Sample.Core
{
    internal class AbortProgressCmd : RCommand 
    {
        protected override void Run() 
        {
            RMVCAppFacade.Instance?.AbortAllCommands();
        }
    }
}
