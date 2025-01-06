﻿using RMVCApp.Sample.Core;
using RMVCApp.Sample.Core.Shared;

namespace RMVCApp.Forms {
    public partial class WeatherView : UserControl, IWeatherView {
        public WeatherView() {
            InitializeComponent();
            Context.RegisterView(this);
        }


        public void SetView(WeatherForecastDTO[]? forecasts) {
            if (this.InvokeRequired) {
                this.Invoke(new Action(() => SetView(forecasts)));
                return;
            }
            dataGridView1.Rows.Clear();

            // Add data to the DataGridView
            foreach (var forecast in forecasts) {
                dataGridView1.Rows.Add(forecast.Date.ToShortDateString(), forecast.TemperatureC, forecast.TemperatureF, forecast.Summary);
            }
        }

        protected void HandleDisposing() {
            Context.UnregisterView(this);
        }
    }
}