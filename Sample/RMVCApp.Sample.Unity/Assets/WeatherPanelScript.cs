#nullable enable

using RMVCApp.Sample.Core;
using RMVCApp.Sample.Core.Shared;
using TMPro;
using UnityEngine;

public class WeatherPanelScript : MonoBehaviour, IWeatherView
{
    [SerializeField] private GameObject weatherTable = null!;       
    [SerializeField] private GameObject weatherRowPrefab = null!;   

    public void SetView(WeatherForecastDTO[]? forecasts)
    {
        UnityMainThreadDispatcher.Enqueue(() =>
        {
            // Clear existing rows, if any.
            foreach (Transform child in weatherTable.transform)
            {
                Destroy(child.gameObject);
            }

            if (forecasts == null)
                return;

            foreach (var forecast in forecasts)
            {
                GameObject row = Instantiate(weatherRowPrefab, weatherTable.transform);

                row.transform.Find("DateText").GetComponent<TextMeshProUGUI>().text = forecast.Date.ToShortDateString();
                row.transform.Find("TempCText").GetComponent<TextMeshProUGUI>().text = forecast.TemperatureC + " °C";
                row.transform.Find("TempFText").GetComponent<TextMeshProUGUI>().text = forecast.TemperatureF + " °F";
                row.transform.Find("SummaryText").GetComponent<TextMeshProUGUI>().text = forecast.Summary ?? "";
            }
        });
    }

    void Start() 
    {
        RMVCAppFacade.RegisterActor<IWeatherView>(this);
    }

    private void OnDestroy() 
    {
        RMVCAppFacade.UnregisterActor<IWeatherView>(this);
    }
}
