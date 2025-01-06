using RMVCApp.RMVC;
using RMVCApp.RMVC.Shared;
using UnityEngine;
using static RMVCApp.RMVC.Shared.Enums;

public class MainCanvasScript : MonoBehaviour, IMainView
{
    [SerializeField] private GameObject HomePanel;
    [SerializeField] private GameObject CounterPanel;
    [SerializeField] private GameObject WeatherPanel;
    public void ShowView(Enums.ViewEnum viewEnum) {

        if (viewEnum == ViewEnum.None || viewEnum == ViewEnum.Progress)
            return;

        UnityMainThreadDispatcher.Enqueue(() => {
            HomePanel.SetActive(false);
            CounterPanel.SetActive(false);
            WeatherPanel.SetActive(false);

            // Activate the selected panel
            switch (viewEnum) {
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

    private void Awake() {
        ShowView(Enums.ViewEnum.Home);
    }

    void Start() {
        Context.RegisterView(this);
    }
    private void OnDestroy() {
        Context.UnregisterView(this);
    }
}