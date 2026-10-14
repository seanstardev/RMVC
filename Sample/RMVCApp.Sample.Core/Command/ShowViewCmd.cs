using System.Threading.Tasks;
using RMVC;
using RMVCApp.Sample.Core.Command;
using static RMVCApp.Sample.Core.Shared.Enums;

namespace RMVCApp.Sample.Core 
{
    internal class ShowViewCmd : RMVCAppCommandBase<RVoid>
    {
        private readonly ViewEnum viewEnum;

        public ShowViewCmd(ViewEnum viewEnum) 
        {
            this.viewEnum = viewEnum;
        }

        protected override async Task<RVoid> Run() 
        {
            base.MainMediator?.ShowView(viewEnum);

            return await Complete();
        }
    }
}
