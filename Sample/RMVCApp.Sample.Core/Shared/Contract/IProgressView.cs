using global::RMVC;
using System;

namespace RMVCApp.Sample.Core.Shared {


    public interface IProgressView : IRContract {
        event Action? AbortProgressEvt;
        void SetProgress(RProgress[] progress);
        void ResetView();
    }
}
