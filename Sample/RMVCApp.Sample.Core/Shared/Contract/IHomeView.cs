
using RMVC;
using System;

namespace RMVCApp.Sample.Core.Shared {
    public interface IHomeView : IRContract {
        event Action<string>? ShowRmvcMessageEvt;
        event Action? RunRmvcProgressTestEvt;
        void SetView();
    }
}
