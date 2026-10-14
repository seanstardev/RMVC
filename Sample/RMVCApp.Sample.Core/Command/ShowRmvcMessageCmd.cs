using System.Threading.Tasks;
using global::RMVC;
using RMVCApp.Sample.Core.Command;

namespace RMVCApp.Sample.Core 
{

    internal class ShowRmvcMessageCmd : RMVCAppCommandBase<RVoid>
    {
        private readonly string message;

        public ShowRmvcMessageCmd(string message) 
        {
            this.message = message;
        }

        protected override async Task<RVoid> Run() 
        {
            if (base.App != null) 
            {
                _ = await base.App.ShowMessageBox("Attention", message, false);
            }

            return await Complete();
        }
    }
}
