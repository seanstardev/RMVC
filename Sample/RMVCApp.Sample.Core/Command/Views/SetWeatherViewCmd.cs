using System.Threading.Tasks;
using RMVC;
using RMVCApp.Sample.Core.Command;

namespace RMVCApp.Sample.Core 
{
    internal class SetWeatherViewCmd : RMVCAppCommandBase<RVoid>
    {
        protected override async Task<RVoid> Run() 
        {
            base.WeatherMediator?.SetView(
                base.WeatherModel?.forecasts ?? null);

            return await Complete();
        }
    }
}
