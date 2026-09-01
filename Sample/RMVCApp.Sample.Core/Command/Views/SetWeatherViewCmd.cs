using RMVC;

namespace RMVCApp.Sample.Core 
{
    internal class SetWeatherViewCmd : RCommand 
    {
        protected override void Run() 
        {
            RMVCAppFacade.Instance?.WeatherMediator?.SetView(
                RMVCAppFacade.Instance?.WeatherModel?.forecasts ?? null);
        }
    }
}
