using RMVCApp.RMVC;
using RMVCApp.RMVC.Shared;
using System;
using UnityEngine;

public class NavigationPanelScript : MonoBehaviour, INavigationView
{
    public event Action ShowHomeViewEvt;
    public event Action ShowCounterViewEvt;
    public event Action ShowWeatherViewEvt;

    public void GoToHome() {
        ShowHomeViewEvt?.Invoke();
    }

    public void GoToCounter() {
        ShowCounterViewEvt?.Invoke();
    }

    public void GoToWeather() {
        ShowWeatherViewEvt?.Invoke();
    }

    void Start()
    {
        Context.RegisterView(this);    
    }
    private void OnDestroy() {
        Context.UnregisterView(this);
    }
}