using RMVC;
using RMVCApp.Sample.Core;
using RMVCApp.Sample.Core.Shared;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace RMVCApp.WPF.ViewModels 
{
    public class ProgressViewModel : INotifyPropertyChanged, IViewLifecycleAware, IProgressView 
    {
        public ObservableCollection<RProgress> ProgressItems { get; } = 
            new ObservableCollection<RProgress>();
        public event PropertyChangedEventHandler? PropertyChanged;
        public ICommand AbortCommand { get; }

        public event Action? AbortProgressEvt;

        public ProgressViewModel() 
        {
            AbortCommand = new RelayCommand(OnAbort);
            UpdateHasProgressItems();
        }
        
        private bool _hasProgressItems;
        
        public bool HasProgressItems 
        {
            get => _hasProgressItems;
            set 
            {
                _hasProgressItems = value;
                OnPropertyChanged();
            }
        }

        public void OnDisposed() 
        {
            RMVCAppFacade.UnregisterActor<IProgressView>(this);
        }

        public void OnInitialised() 
        {
            RMVCAppFacade.RegisterActor<IProgressView>(this);
        }

        public void SetProgress(RProgress[] progress) 
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                ProgressItems.Clear();

                foreach (var item in progress) {
                    ProgressItems.Add(item);
                }

                UpdateHasProgressItems();
            });
        }

        public void ResetView() 
        {
            Application.Current.Dispatcher.BeginInvoke(() => 
            {
                ProgressItems.Clear();

                UpdateHasProgressItems();
            });
        }

        private void OnAbort() 
        {
            AbortProgressEvt?.Invoke();
        }
        
        private void UpdateHasProgressItems() 
        {
            HasProgressItems = ProgressItems.Count > 0;
        }
        protected void OnPropertyChanged([CallerMemberName] string? name = null) 
        {
            PropertyChanged?.Invoke(
                this, new PropertyChangedEventArgs(name));
        }
    }
}