using RMVC;
using System;

namespace RMVCApp.Sample.Core.Shared {
    public interface ICounterView : IRContract {
        event Action<int>? SetCounterEvt;
        void SetCounter(int count);
    }
}
