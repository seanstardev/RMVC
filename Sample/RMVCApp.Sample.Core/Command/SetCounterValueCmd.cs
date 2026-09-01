using RMVC;

namespace RMVCApp.Sample.Core 
{
    internal class SetCounterValueCmd : RCommand 
    {
        private readonly int counterCount;

        public SetCounterValueCmd(int counterCount) 
        {
            this.counterCount = counterCount;
        }

        protected override void Run() 
        {
            if (RMVCAppFacade.Instance?.CounterModel != null) 
            {
                RMVCAppFacade.Instance.CounterModel.CounterCount = counterCount;
            }
        }
    }
}
