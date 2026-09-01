using RMVC;
using RMVCApp.Sample.Core.Shared;
using System;

namespace RMVCApp.Sample.Core 
{
    internal class WeatherMediator : RMediator 
    {
        private IWeatherView? view => (IWeatherView?)base.viewBase;
        public WeatherMediator(Type view) : base(view) 
        { 
        }

        public void SetView(WeatherForecastDTO[]? forecasts) 
        {
            view?.SetView(forecasts);
        }

        protected override void Initialsed() 
        {
            base.ExecuteCommand(new SetWeatherViewCmd());
        }
        protected override void Disposing() 
        {

        }
    }
}
