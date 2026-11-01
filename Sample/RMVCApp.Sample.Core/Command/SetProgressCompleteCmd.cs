using System.Threading.Tasks;
using RMVC;
using RMVCApp.Sample.Core.Command;

namespace RMVCApp.Sample.Core 
{
    internal class SetProgressCompleteCmd : RMVCAppCommandBase<RVoid>
    {
        private string commandRootId;

        public SetProgressCompleteCmd(string commandRootId)
        {
            this.commandRootId = commandRootId;
        }

        protected override async Task<RVoid> Run() 
        {
            await base.Execute(new ShowViewCmd(Shared.Enums.ViewEnum.None));

            base.ProgressMediator?.ResetView();

            return await Complete();
        }
    }
}
