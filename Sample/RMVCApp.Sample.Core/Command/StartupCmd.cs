using System.Threading.Tasks;
using RMVC;
using RMVCApp.Sample.Core.Command;

namespace RMVCApp.Sample.Core 
{
    internal class StartupCmd : RMVCAppCommandBase<RVoid>
    {
        protected override async Task<RVoid> Run() 
        {
            return await Complete();
        }
    }
}
