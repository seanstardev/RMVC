using System.Threading.Tasks;
using RMVC;
using RMVCApp.Sample.Core.Command;

namespace RMVCApp.Sample.Core 
{
    internal class SetCounterViewCmd : RMVCAppCommandBase<RVoid>
    {
        protected override async Task<RVoid> Run() 
        {
            base.CounterMediator?.SetView(
                base.CounterModel?.CounterCount ?? 0);

            return await Complete();
        }
    }
}
