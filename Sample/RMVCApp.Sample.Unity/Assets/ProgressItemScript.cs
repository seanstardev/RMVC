#nullable enable

using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ProgressItemScript : MonoBehaviour {
    [SerializeField] private TMP_Text headingText = null!;
    [SerializeField] private TMP_Text percentText = null!;
    [SerializeField] private Slider progressSlider = null!;

    public void UpdateProgress(string heading, int percent) {
        headingText.text = heading;
        percentText.text = $"{percent}%";
        //progressSlider.value = percent;
        progressSlider.SetValueWithoutNotify(percent);
    }
}