using RMVCApp.Sample.Core;
using RMVCApp.Sample.Core.Shared;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace RMVCApp.WPF.ViewModels 
{
    public class WeatherViewModel : INotifyPropertyChanged, IViewLifecycleAware, IWeatherView 
    {
        private ObservableCollection<WeatherForecastDTO> _forecasts = 
            new ObservableCollection<WeatherForecastDTO>();

        public ObservableCollection<WeatherForecastDTO> Forecasts 
        {
            get => _forecasts;
            set 
            { 
                _forecasts = value; 
                OnPropertyChanged(); 
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public void SetView(WeatherForecastDTO[]? forecasts)
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                Forecasts.Clear();

                if (forecasts != null)
                {
                    foreach (var forecast in forecasts)
                    {
                        Forecasts.Add(forecast);
                    }
                }
            });
        }

        public void OnInitialised() 
        {
            RMVCAppFacade.RegisterActor<IWeatherView>(this);
        }

        public void OnDisposed() 
        {
            RMVCAppFacade.UnregisterActor<IWeatherView>(this);
        }

        protected void OnPropertyChanged([CallerMemberName] string? name = null) 
        {
            PropertyChanged?.Invoke(
                this, new PropertyChangedEventArgs(name));
        }
    }
}
