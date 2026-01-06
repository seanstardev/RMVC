using RMVC;
using System;

namespace RMVCApp.Sample.Core.Shared {
    public interface INavigationView : IRContract
    {
        event Action? ShowHomeViewEvt;
        event Action? ShowCounterViewEvt;
        event Action? ShowWeatherViewEvt;
    }
}
