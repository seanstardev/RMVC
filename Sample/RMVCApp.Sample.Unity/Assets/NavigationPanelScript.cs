#nullable enable

using RMVCApp.Sample.Core;
using RMVCApp.Sample.Core.Shared;
using System;
using UnityEngine;

public class NavigationPanelScript : MonoBehaviour, INavigationView
{
    public event Action? ShowHomeViewEvt;
    public event Action? ShowCounterViewEvt;
    public event Action? ShowWeatherViewEvt;

    public void GoToHome() 
    {
        ShowHomeViewEvt?.Invoke();
    }

    public void GoToCounter() 
    {
        ShowCounterViewEvt?.Invoke();
    }

    public void GoToWeather() 
    {
        ShowWeatherViewEvt?.Invoke();
    }

    void Start()
    {
        RMVCAppFacade.RegisterActor<INavigationView>(this);    
    }
    
    private void OnDestroy() 
    {
        RMVCAppFacade.UnregisterActor<INavigationView>(this);
    }
}
