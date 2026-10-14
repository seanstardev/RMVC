using RMVC;
using RMVCApp.Sample.Core.Shared;

namespace RMVCApp.Sample.Core 
{
    internal class WeatherMediator : RMediator<IWeatherView>
    {
        public void SetView(WeatherForecastDTO[]? forecasts) 
        {
            actor?.SetView(forecasts);
        }

        protected override void Initialised() 
        {
            base.Execute(new SetWeatherViewCmd());
        }
        protected override void Disposing() 
        {

        }
    }
}
