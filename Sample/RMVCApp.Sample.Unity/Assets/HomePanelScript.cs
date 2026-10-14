#nullable enable

using RMVCApp.Sample.Core;
using RMVCApp.Sample.Core.Shared;
using System;
using UnityEngine;

public class HomePanelScript : MonoBehaviour, IHomeView
{
    public event Action<string>? ShowRmvcMessageEvt;
    public event Action? RunRmvcProgressTestEvt;

    public void SetView() 
    {

    }
    
    public void OnShowRmvcTestMessage() 
    {
        ShowRmvcMessageEvt?.Invoke("Hello from RMVC.");
    }
    
    public void RunRmvcProgressTest() 
    {
        RunRmvcProgressTestEvt?.Invoke();
    }
    
    void Start() 
    {
        RMVCAppFacade.RegisterActor<IHomeView>(this);
    }
    private void OnDestroy() 
    {
        RMVCAppFacade.UnregisterActor<IHomeView>(this);
    }
}
