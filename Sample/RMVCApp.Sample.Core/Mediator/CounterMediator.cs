using RMVC;
using RMVCApp.Sample.Core.Shared;

namespace RMVCApp.Sample.Core 
{
    internal class CounterMediator : RMediator<ICounterView>
    {
        internal void SetView(int counterCount) 
        {
            actor?.SetCounter(counterCount);
        }
        
        private void OnSetCounter(int count) 
        {
            base.Execute(new SetCounterValueCmd(count));
        }

        protected override void Initialised() 
        {
            if (actor != null) 
            {
                actor.SetCounterEvt += OnSetCounter;
            }
            base.Execute(new SetCounterViewCmd());
        }

        protected override void Disposing() 
        {
            if (actor != null) 
            {
                actor.SetCounterEvt -= OnSetCounter;
            }
        }
    }
}
