#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public class UnityMainThreadDispatcher : MonoBehaviour 
{
    private static readonly Queue<Func<Task>> _executionQueue = new();
    private static UnityMainThreadDispatcher? _instance;
    // Non-async Enqueue
    
    public static void Enqueue(Action action) 
    {
        lock (_executionQueue) 
        {
            _executionQueue.Enqueue(() => 
            {
                action();
                return Task.CompletedTask; // Support Task compatibility
            });
        }
    }

    // Enqueue async methods with result (no need for lambda)
    public static Task<T> EnqueueAsync<T>(Func<Task<T>> action) 
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        Enqueue(() => 
        {
            ExecuteAsync(action, tcs); // Handle async execution
        });

        return tcs.Task; // Return the Task directly
    }

    // Enqueue async methods without results
    public static Task EnqueueAsync(Func<Task> action) 
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        Enqueue(() => 
        {
            ExecuteAsync(action, tcs); // Handle async execution
        });

        return tcs.Task; // Return the Task directly
    }

    // Internal execution wrappers
    private static async void ExecuteAsync(Func<Task> action, TaskCompletionSource<bool> tcs) 
    {
        try 
        {
            await action();
            tcs.TrySetResult(true);
        }
        catch (Exception ex) 
        {
            tcs.TrySetException(ex);
        }
    }

    private static async void ExecuteAsync<T>(Func<Task<T>> action, TaskCompletionSource<T> tcs) 
    {
        try 
        {
            var result = await action();
            tcs.TrySetResult(result);
        }
        catch (Exception ex) 
        {
            tcs.TrySetException(ex);
        }
    }


    // Call from the Unity main thread before Facade creation.
    public static void Initialise() 
    {
        if (_instance == null) 
        {
            var obj = new GameObject("MainThreadDispatcher");
            _instance = obj.AddComponent<UnityMainThreadDispatcher>();
            DontDestroyOnLoad(obj);
        }
    }

    private void Update()
    {
        while (true)
        {
            Func<Task> action;

            lock (_executionQueue)
            {
                if (_executionQueue.Count == 0)
                    return;

                action = _executionQueue.Dequeue();
            }

            _ = action();
        }
    }
}
