#nullable enable

using System;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MessageBoxScript : MonoBehaviour {
    [SerializeField] private TMP_Text TitleText = null!;
    [SerializeField] private TMP_Text MessageText = null!;
    [SerializeField] private Button YesButton = null!;    
    [SerializeField] private Button NoButton = null!;     
    [SerializeField] private Button OkButton = null!;     

    private TaskCompletionSource<bool>? _taskCompletionSource;

    // Initialize the message box
    public Task<bool> Show(string title, string message, bool isYesNo) {
        Debug.Log($"2 >>>> Displaying MessageBox: {title}");
        // Update UI content
        TitleText.text = title;
        MessageText.text = message;

        // Show relevant buttons
        YesButton.gameObject.SetActive(isYesNo);
        NoButton.gameObject.SetActive(isYesNo);
        OkButton.gameObject.SetActive(!isYesNo);

        // Prepare the TaskCompletionSource for async handling
        _taskCompletionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Attach listeners
        YesButton.onClick.AddListener(OnYesClicked);
        NoButton.onClick.AddListener(OnNoClicked);
        OkButton.onClick.AddListener(OnOkClicked);

        // Show the GameObject
        gameObject.SetActive(true);

        // Return the task that completes on button click
        return _taskCompletionSource.Task;
    }

    // Button Handlers
    private void OnYesClicked() {
        Close(true); // Yes = true
    }

    private void OnNoClicked() {
        Close(false); // No = false
    }

    private void OnOkClicked() {
        Close(true); // OK = true
    }

    // Cleanup and hide the MessageBox
    private void Close(bool result) {
        // Remove listeners to prevent duplicate handling
        YesButton.onClick.RemoveAllListeners();
        NoButton.onClick.RemoveAllListeners();
        OkButton.onClick.RemoveAllListeners();

        // Hide the GameObject
        gameObject.SetActive(false);

        // Complete the task
        _taskCompletionSource?.TrySetResult(result);
    }
}
