using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RMVCApp.Sample.Core;
using RMVCApp.Sample.Core.Shared;

namespace RMVCApp.Avalonia.ViewModels {



    public partial class CounterViewModel : ViewModelBase, ICounterView {

        public event Action<int>? SetCounterEvt;

        [ObservableProperty]
        private int _count;

        public CounterViewModel() {

        }


        public void SetCounter(int count) {
            Count = count;
        }

        [RelayCommand]
        private void IncrementCount() {
            Count++;
            SetCounterEvt?.Invoke(Count);
        }

        protected internal override void OnViewDisposed() {

            RMVCAppFacade.UnregisterActor<ICounterView>(this);

        }

        protected internal override void OnViewInitialized() {
            RMVCAppFacade.RegisterActor<ICounterView>(this);
        }
    }
}
