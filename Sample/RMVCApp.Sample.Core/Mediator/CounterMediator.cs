using RMVC;
using RMVCApp.Sample.Core.Shared;
using System;

namespace RMVCApp.Sample.Core {
    internal class CounterMediator : RMediator 
    {
        private ICounterView? view => (ICounterView?)base.viewBase;

        public CounterMediator(Type view) : base(view) 
        { 
        }

        internal void SetView(int counterCount) 
        {
            view?.SetCounter(counterCount);
        }
        
        private void OnSetCounter(int count) 
        {
            base.ExecuteCommand(new SetCounterValueCmd(count));
        }

        protected override void Initialsed() 
        {
            if (view != null) 
            {
                view.SetCounterEvt += OnSetCounter;
            }
            base.ExecuteCommand(new SetCounterViewCmd());
        }

        protected override void Disposing() 
        {
            if (view != null) 
            {
                view.SetCounterEvt -= OnSetCounter;
            }
        }

    }
}
