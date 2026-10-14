#nullable enable

using RMVC;
using RMVCApp.Sample.Core;
using RMVCApp.Sample.Core.Shared;
using System;
using System.Collections.Generic;
using UnityEngine;

public class ProgressPanelScript : MonoBehaviour, IProgressView 
{
    public event Action? AbortProgressEvt;

    [SerializeField] private GameObject progressItemPrefab = null!; // Prefab for items
    [SerializeField] private Transform progressLayoutGroup = null!; // Parent with Vertical Layout Group

    private readonly Dictionary<string, ProgressItemScript> progressItems = new();

    public void ResetView() 
    {
        UnityMainThreadDispatcher.Enqueue(() => 
        {
            // Destroy all existing progress items and clear the cache
            foreach (Transform child in progressLayoutGroup) 
            {
                Destroy(child.gameObject);
            }
            progressItems.Clear();
        });
    }

    public void SetProgress(RProgress[] progress) 
    {
        UnityMainThreadDispatcher.Enqueue(() => 
        {
            // Clear all progress items and start fresh
            foreach (Transform child in progressLayoutGroup) 
            {
                Destroy(child.gameObject);
            }
            progressItems.Clear();

            // Recreate items from scratch
            foreach (var p in progress) 
            {
                // Instantiate a new progress item
                var item = Instantiate(progressItemPrefab, progressLayoutGroup);
                var progressScript = item.GetComponent<ProgressItemScript>();

                // Initialize values immediately to avoid jumps
                progressScript.UpdateProgress(p.Heading, p.Percent);

                // Cache the newly created item
                progressItems[p.Id] = progressScript;
            }
        });
    }

    public void OnAbortProgress() 
    {
        AbortProgressEvt?.Invoke();
    }

    void Start() 
    {
        ResetView();
        RMVCAppFacade.RegisterActor<IProgressView>(this);
    }

    private void OnDestroy() 
    {
        RMVCAppFacade.UnregisterActor<IProgressView>(this);
    }
}
