using RMVC;

namespace RMVCApp.Sample.Core 
{
    internal class SetProgressViewCmd : RCommand 
    {
        public SetProgressViewCmd() 
        {
        }

        protected override void Run() 
        {
            if (RMVCAppFacade.Instance?.ProgressModel != null)
                RMVCAppFacade.Instance.ProgressMediator?.SetView(RMVCAppFacade.Instance.ProgressModel.Progress);
        }
    }
}
