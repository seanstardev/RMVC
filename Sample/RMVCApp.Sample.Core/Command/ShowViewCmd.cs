using RMVC;
using static RMVCApp.Sample.Core.Shared.Enums;

namespace RMVCApp.Sample.Core 
{
    internal class ShowViewCmd : RCommand 
    {
        private readonly ViewEnum viewEnum;

        public ShowViewCmd(ViewEnum viewEnum) 
        {
            this.viewEnum = viewEnum;
        }

        protected override void Run() 
        {
            RMVCAppFacade.Instance?.MainMediator?.ShowView(viewEnum);
        }
    }
}
