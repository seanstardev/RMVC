using System.Collections;
using System.Reflection;
using RMVC;

namespace RMVC.ContractLookupChecks;

// Test-only observation: lifecycle Running is not a command-completed notification.
internal static class TestRuntime
{
    internal static bool IsIdle
    {
        get
        {
            const BindingFlags sf = BindingFlags.Static | BindingFlags.NonPublic;
            var completion = (TaskCompletionSource<bool>)typeof(RFacade)
                .GetField("startupFinished", sf)!.GetValue(null)!;
            Task startup = completion.Task;
            if (!startup.IsCompleted) return false;
            object registry = typeof(RFacade).GetField("operations", sf)!.GetValue(null)!;
            const BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic;
            object gate = registry.GetType().GetField("operationGate", f)!.GetValue(registry)!;
            lock (gate) return ((IDictionary)registry.GetType().GetField("activeOperations", f)!.GetValue(registry)!).Count == 0;
        }
    }
    internal static async Task WaitIdle()
    {
        var limit = DateTime.UtcNow.AddSeconds(10);
        while (!IsIdle)
        {
            if (DateTime.UtcNow > limit) throw new TimeoutException("Test runtime did not become idle.");
            await Task.Delay(10);
        }
    }
    // Test-only registry inspection; production facades no longer expose these lookups.
    private static object? Resolve(RFacade facade, string method, Type requested)
    {
        try
        {
            return typeof(RFacade).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
                .MakeGenericMethod(requested).Invoke(facade, null);
        }
        catch (TargetInvocationException error) { throw error.InnerException!; }
    }
    internal static T GetModel<T>(this RFacade facade) where T : class, IRModel
        => (T)Resolve(facade, "ResolveModel", typeof(T))!;
    internal static T GetMediator<T>(this RFacade facade) where T : RMediator
        => (T)Resolve(facade, "ResolveMediator", typeof(T))!;
    internal static T? FindModel<T>(this RFacade facade) where T : class
        => (T?)Resolve(facade, "ResolveModelByContract", typeof(T));
    internal static T? FindMediator<T>(this RFacade facade) where T : class
        => (T?)Resolve(facade, "ResolveMediatorByContract", typeof(T));
    internal static IRShell ReadShell()
        => (IRShell)typeof(RFacade).GetProperty("AppShell", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
}
