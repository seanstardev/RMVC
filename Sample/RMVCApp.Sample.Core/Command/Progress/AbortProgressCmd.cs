using System.Threading.Tasks;
using RMVC;
using RMVCApp.Sample.Core.Command;

namespace RMVCApp.Sample.Core
{
    internal class AbortProgressCmd : RMVCAppCommandBase<RVoid>
    {
        protected override async Task<RVoid> Run()
        {
            base.AbortAllCommands();

            return await Complete();
        }
    }
}