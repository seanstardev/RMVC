using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RMVC;
using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using RMVCApp.Sample.Core.Shared;
using RMVCApp.Sample.Core;

namespace RMVCApp.Avalonia.ViewModels {
    public partial class ProgressViewModel : ViewModelBase, IProgressView 
    {

        public event Action? AbortProgressEvt;

        public ObservableCollection<RProgress> ProgressItems { get; } = new();

        [ObservableProperty]
        private bool hasProgressItems;

        public ProgressViewModel() { }

        [RelayCommand]
        private void AbortProgress() 
        {
            AbortProgressEvt?.Invoke();
        }

        public void ResetView() 
        {
            Dispatcher.UIThread.Post(() => 
            {
                ProgressItems.Clear();
                UpdateHasProgressItems();
            });
        }

        public void SetProgress(RProgress[] progress) 
        {
            // Ensure UI thread execution
            Dispatcher.UIThread.Post(() =>
            {
                ProgressItems.Clear();
                foreach (var item in progress) {
                    ProgressItems.Add(item);
                }
                UpdateHasProgressItems();
            });
        }

        private void UpdateHasProgressItems() 
        {
            HasProgressItems = ProgressItems.Count > 0;
        }

        protected internal override void OnViewDisposed() 
        {
            RMVCAppFacade.UnregisterActor<IProgressView>(this);
        }

        protected internal override void OnViewInitialized() 
        {
            RMVCAppFacade.RegisterActor<IProgressView>(this);
        }
    }
}
