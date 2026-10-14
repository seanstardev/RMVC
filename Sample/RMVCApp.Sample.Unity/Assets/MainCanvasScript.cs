#nullable enable

using RMVCApp.Sample.Core;
using RMVCApp.Sample.Core.Shared;
using UnityEngine;
using static RMVCApp.Sample.Core.Shared.Enums;

public class MainCanvasScript : MonoBehaviour, IMainView
{
    [SerializeField] private GameObject HomePanel = null!;
    [SerializeField] private GameObject CounterPanel = null!;
    [SerializeField] private GameObject WeatherPanel = null!;

    public void ShowView(Enums.ViewEnum viewEnum) 
    {
        if (viewEnum == ViewEnum.None || viewEnum == ViewEnum.Progress)
            return;

        UnityMainThreadDispatcher.Enqueue(() => 
        {
            HomePanel.SetActive(false);
            CounterPanel.SetActive(false);
            WeatherPanel.SetActive(false);

            // Activate the selected panel
            switch (viewEnum) 
            {
                case Enums.ViewEnum.Home:
                    HomePanel.SetActive(true);
                    break;
                case Enums.ViewEnum.Counter:
                    CounterPanel.SetActive(true);
                    break;
                case Enums.ViewEnum.Weather:
                    WeatherPanel.SetActive(true);
                    break;
            }
        });
    }

    private void Awake() 
    {
        ShowView(Enums.ViewEnum.Home);
    }

    void Start() 
    {
        RMVCAppFacade.RegisterActor<IMainView>(this);
    }
    
    private void OnDestroy() 
    {
        RMVCAppFacade.UnregisterActor<IMainView>(this);
    }
}
