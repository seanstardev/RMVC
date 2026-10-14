using RMVC;
using RMVCApp.Sample.Core.Shared;

namespace RMVCApp.Sample.Core 
{
    internal class NavigationMediator : RMediator<INavigationView>
    {
        protected override void Initialised() 
        {
            if (actor != null) {
                actor.ShowHomeViewEvt += OnShowHome;
                actor.ShowCounterViewEvt += OnShowCounter;
                actor.ShowWeatherViewEvt += OnShowWeather;
            }
        }

        protected override void Disposing() 
        {
            if (actor != null) 
            {
                actor.ShowHomeViewEvt -= OnShowHome;
                actor.ShowCounterViewEvt -= OnShowCounter;
                actor.ShowWeatherViewEvt -= OnShowWeather;
            }
        }

        private void OnShowHome() 
        {
            base.Execute(new ShowViewCmd(Enums.ViewEnum.Home));
        }

        private void OnShowCounter() 
        {
            base.Execute(new ShowViewCmd(Enums.ViewEnum.Counter));
        }

        private void OnShowWeather() 
        {
            base.Execute(new ShowViewCmd(Enums.ViewEnum.Weather));
        }
    }
}
