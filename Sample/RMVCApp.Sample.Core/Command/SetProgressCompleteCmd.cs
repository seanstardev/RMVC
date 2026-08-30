using RMVC;

namespace RMVCApp.Sample.Core 
{
    internal class SetProgressCompleteCmd : RCommand 
    {
        protected override void Run() 
        {
            base.ExecuteCommand(new ShowViewCmd(Shared.Enums.ViewEnum.None));
            RMVCAppFacade.Instance?.ProgressMediator?.ResetView();
        }
    }
}
