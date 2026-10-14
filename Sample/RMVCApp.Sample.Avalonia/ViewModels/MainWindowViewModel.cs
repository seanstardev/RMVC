using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RMVCApp.Sample.Core;
using RMVCApp.Sample.Core.Shared;

namespace RMVCApp.Avalonia.ViewModels 
{
    public partial class MainWindowViewModel : ViewModelBase, IMainView 
    {
        [ObservableProperty]
        private object? _currentView;

        public MainWindowViewModel() 
        {
            NavigateToHome();
        }

        // Property to get the app name from the system
        public string AppName => Application.Current?.Name ?? "Demo App";

        [RelayCommand]
        private void NavigateToHome() 
        {
            CurrentView = new HomeViewModel();
        }

        [RelayCommand]
        private void NavigateToCounter() 
        {
            CurrentView = new CounterViewModel();
        }

        [RelayCommand]
        private void NavigateToWeather() 
        {
            CurrentView = new WeatherViewModel();
        }

        public void ShowView(Enums.ViewEnum viewEnum) 
        {

        }

        protected internal override void OnViewDisposed() 
        {
            RMVCAppFacade.UnregisterActor<IMainView>(this);
        }

        protected internal override void OnViewInitialized() {

            RMVCAppFacade.RegisterActor<IMainView>(this);
        }






    }
}
