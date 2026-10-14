#nullable enable

using RMVCApp.Sample.Core;
using RMVCApp.Sample.Core.Shared;
using System;
using TMPro;
using UnityEngine;

public class CounterPanelScript : MonoBehaviour, ICounterView
{
    [SerializeField] private TextMeshProUGUI counterText = null!;
    
    public event Action<int>? SetCounterEvt;

    private int count = 0;

    public void SetCounter(int count) 
    {
        UnityMainThreadDispatcher.Enqueue(() =>
        {
            this.count = count;
            counterText.text = count.ToString();
        });
    }
    
    public void OnIncrementCounter() 
    {
        count++;
        counterText.text = count.ToString();
        SetCounterEvt?.Invoke(count);
    }

    void Start() 
    {
        RMVCAppFacade.RegisterActor<ICounterView>(this);
    }
    
    private void OnDestroy() 
    {
        RMVCAppFacade.UnregisterActor<ICounterView>(this);
    }
}
