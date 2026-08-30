using RMVC;

namespace RMVCApp.Sample.Core 
{
    internal class SetCounterViewCmd : RCommand 
    {
        protected override void Run() 
        {
            RMVCAppFacade.Instance?.CounterMediator?.SetView(
                RMVCAppFacade.Instance?.CounterModel?.CounterCount ?? 0);
        }
    }
}
