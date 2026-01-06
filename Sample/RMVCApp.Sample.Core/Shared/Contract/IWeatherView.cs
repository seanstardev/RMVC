using RMVC;

namespace RMVCApp.Sample.Core.Shared {
    public interface IWeatherView : IRContract {
        void SetView(WeatherForecastDTO[]? Forecasts);
    }
}
