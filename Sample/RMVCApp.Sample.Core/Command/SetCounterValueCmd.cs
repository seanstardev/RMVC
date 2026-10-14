using System.Threading.Tasks;
using RMVC;
using RMVCApp.Sample.Core.Command;

namespace RMVCApp.Sample.Core 
{
    internal class SetCounterValueCmd : RMVCAppCommandBase<RVoid>
    {
        private readonly int counterCount;

        public SetCounterValueCmd(int counterCount) 
        {
            this.counterCount = counterCount;
        }

        protected override async Task<RVoid> Run() 
        {
            if (base.CounterModel != null) 
            {
                base.CounterModel.CounterCount = counterCount;
            }

            return await Complete();
        }
    }
}
