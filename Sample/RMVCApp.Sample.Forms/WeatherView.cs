using RMVCApp.Sample.Core;
using RMVCApp.Sample.Core.Shared;

namespace RMVCApp.Forms {
    public partial class WeatherView : UserControl, IWeatherView
    {
        public WeatherView() 
        {
            InitializeComponent();
            RMVCAppFacade.RegisterActor(this);
        }

        public void SetView(WeatherForecastDTO[]? forecasts) 
        {
            if (this.InvokeRequired) 
            {
                this.Invoke(new Action(() => SetView(forecasts)));
                return;
            }
            dataGridView1.Rows.Clear();

            if (forecasts != null)
            {
                // Add data to the DataGridView
                foreach (var forecast in forecasts)
                {
                    dataGridView1.Rows.Add(forecast.Date.ToShortDateString(), forecast.TemperatureC, forecast.TemperatureF, forecast.Summary);
                }
            }
        }

        protected void HandleDisposing() 
        {
            RMVCAppFacade.UnregisterActor(this);
        }
    }
}
