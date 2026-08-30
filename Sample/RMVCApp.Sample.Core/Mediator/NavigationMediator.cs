using RMVC;
using RMVCApp.Sample.Core.Shared;
using System;

namespace RMVCApp.Sample.Core 
{
    internal class NavigationMediator : RMediator 
    {
        private INavigationView? view => (INavigationView?)base.viewBase;

        public NavigationMediator(Type view) : base(view) 
        {
        }

        protected override void Initialsed() 
        {
            if (view != null) {
                view.ShowHomeViewEvt += OnShowHome;
                view.ShowCounterViewEvt += OnShowCounter;
                view.ShowWeatherViewEvt += OnShowWeather;
            }
        }

        protected override void Disposing() 
        {
            if (view != null) 
            {
                view.ShowHomeViewEvt -= OnShowHome;
                view.ShowCounterViewEvt -= OnShowCounter;
                view.ShowWeatherViewEvt -= OnShowWeather;
            }
        }

        private void OnShowHome() 
        {
            base.ExecuteCommand(new ShowViewCmd(Enums.ViewEnum.Home));
        }

        private void OnShowCounter() 
        {
            base.ExecuteCommand(new ShowViewCmd(Enums.ViewEnum.Counter));
        }
        private void OnShowWeather() 
        {
            base.ExecuteCommand(new ShowViewCmd(Enums.ViewEnum.Weather));
        }
    }
}
