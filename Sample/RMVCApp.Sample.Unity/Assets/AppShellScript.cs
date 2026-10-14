#nullable enable

using Assets.Util;
using RMVCApp.Sample.Core;
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class AppShell : MonoBehaviour, IAppShell
{
    [SerializeField] private MessageBoxScript MessageBox = null!;

    private readonly SemaphoreSlim messageBoxGate = new(1, 1);

    private void Awake() 
    {
        var gameObjects = UnityEngine.Object.FindObjectsByType<AppShell>(FindObjectsSortMode.None);
        
        if (gameObjects.Length > 1) 
        {
            Destroy(gameObject); // Prevent duplicate persistent objects
            return;
        }

        DontDestroyOnLoad(gameObject);
        Console.SetOut(new DebugOutputMonitor());
        UnityMainThreadDispatcher.Initialise();
        RMVCAppFacade.Create<RMVCAppFacade>(this);
    }

    public async Task<bool> ShowMessageBox(string title, string message, bool isYesNo) 
    {
        await messageBoxGate.WaitAsync();

        try
        {
            return await UnityMainThreadDispatcher.EnqueueAsync(
                () => MessageBox.Show(title, message, isYesNo));
        }
        finally
        {
            messageBoxGate.Release();
        }
    }

    public void HandleOperationsStarted()
    {

    }

    public void HandleOperationsCompleted()
    {

    }
}
