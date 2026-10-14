using System.Threading.Tasks;
using RMVC;
using RMVCApp.Sample.Core.Command;

namespace RMVCApp.Sample.Core 
{
    internal class SetProgressViewCmd : RMVCAppCommandBase<RVoid>
    {
        protected override async Task<RVoid> Run() 
        {
            if (base.ProgressModel != null)
                base.ProgressMediator?.SetView(base.ProgressModel.Progress);

            return await Complete();
        }
    }
}
