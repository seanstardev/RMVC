using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RMVCApp.Sample.Core;
using RMVCApp.Sample.Core.Shared;
using System;

namespace RMVCApp.Avalonia.ViewModels {
    public partial class HomeViewModel : ViewModelBase, IHomeView 
    {

        [ObservableProperty]
        private string welcomeMessage = "Welcome to the Home Page!";

        
        public event Action<string>? ShowRmvcMessageEvt;
        public event Action? RunRmvcProgressTestEvt;

        public void SetView() 
        {

        }
        protected internal override void OnViewDisposed() 
        {
            RMVCAppFacade.UnregisterActor<IHomeView>(this);
        }

        protected internal override void OnViewInitialized() 
        {
            RMVCAppFacade.RegisterActor<IHomeView>(this);
        }

        [RelayCommand]
        public void NavigateToRmvcTest() 
        {
            ShowRmvcMessageEvt?.Invoke("Hello from RMVC.");
        }

        [RelayCommand]
        public void RunRmvcProgressTestCommand() 
        {
            RunRmvcProgressTestEvt?.Invoke();
        }
    }
}
