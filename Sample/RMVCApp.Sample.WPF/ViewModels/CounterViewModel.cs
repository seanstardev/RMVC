using RMVCApp.Sample.Core;
using RMVCApp.Sample.Core.Shared;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace RMVCApp.WPF.ViewModels
{
    public class CounterViewModel : INotifyPropertyChanged, IViewLifecycleAware, ICounterView 
    {
        public event Action<int>? SetCounterEvt;

        private int _count;
        
        public int Count 
        {
            get => _count;
            set 
            { 
                _count = value; 
                OnPropertyChanged(); 
            }
        }

        public ICommand IncrementCommand { get; }

        public CounterViewModel() 
        {
            IncrementCommand = new RelayCommand(() => 
            {
                Count++;
                SetCounterEvt?.Invoke(Count);
            });
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public void SetCounter(int count)
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                Count = count;
            });
        }

        public void OnInitialised() 
        {
            RMVCAppFacade.RegisterActor<ICounterView>(this);
        }

        public void OnDisposed() 
        {
            RMVCAppFacade.UnregisterActor<ICounterView>(this);
        }

        protected void OnPropertyChanged([CallerMemberName] string? name = null) 
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
