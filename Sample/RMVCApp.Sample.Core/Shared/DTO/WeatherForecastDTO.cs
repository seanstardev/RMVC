using System;

namespace RMVCApp.Sample.Core.Shared 
{
    public class WeatherForecastDTO 
    {
        public WeatherForecastDTO(DateTime date, int temperatureC, string? summary) 
        {
            Date = date.Date;
            TemperatureC = temperatureC;
            Summary = summary;
        }

        public DateTime Date { get; }
        public int TemperatureC { get; }
        public string? Summary { get; }
        public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
    }
}
