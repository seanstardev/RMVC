using RMVC;
using System.Linq;
using System;
using RMVCApp.Sample.Core.Shared;

namespace RMVCApp.Sample.Core 
{
    internal class WeatherModel : IRModel 
    {
        public WeatherForecastDTO[]? forecasts { get; private set; }
        private static readonly Random _random = new Random();

        public WeatherModel()
        {
            var startDate = DateTime.Now.Date;
            var summaries = new[] { "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching" };

            forecasts = Enumerable.Range(1, 5).Select(
                index => new WeatherForecastDTO(
                    startDate.AddDays(index),
                    _random.Next(-20, 55), // Use _random instance for random number generation
                    summaries[_random.Next(summaries.Length)])).ToArray();

        }
    }
}
