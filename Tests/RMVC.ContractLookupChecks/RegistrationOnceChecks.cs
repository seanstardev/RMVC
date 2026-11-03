using System.Reflection;
using RMVC;

namespace RMVC.ContractLookupChecks;

internal static class RegistrationOnceChecks
{
    private static string category = "";
    private static bool fail;
    private static readonly TaskCompletionSource entered = Signal();
    private static readonly ManualResetEventSlim release = new();
    private static readonly int[] calls = new int[3];
    private static int repeatedFactoryCalls, updates, completions;
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Check(bool value, string message) => Program.Check(value, category + ": " + message);

    internal static async Task Run(string mode)
    {
        category = mode.Replace("-fail", ""); fail = mode.EndsWith("-fail");
        var shell = new Shell();
        Task creation = Task.Run(() => RFacade.Create<Host>(shell));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(6));
        int index = category == "models" ? 0 : category == "mediators" ? 1 : 2;
        var overlaps = new[]
        {
            Task.Run(() => Rejected(category)),
            Task.Run(() => Rejected(category))
        };
        bool[] rejected = await Task.WhenAll(overlaps).WaitAsync(TimeSpan.FromSeconds(3));
        Check(rejected.All(x => x), "overlapping repeat registration throws before invoking the factory");
        Check(calls[index] == 1 && repeatedFactoryCalls == 0, "the first factory remains the sole invocation while it is running");
        release.Set();
        await creation.WaitAsync(TimeSpan.FromSeconds(5));
        var state = await shell.Settled.Task.WaitAsync(TimeSpan.FromSeconds(6));
        Check(state == (fail ? RApplicationState.Failed : RApplicationState.Running), "original registration succeeds or fails independently of rejected repeats");
        Check(Rejected(category) && calls[index] == 1 && repeatedFactoryCalls == 0,
            "a completed or failed registration attempt never grants a second attempt");
        if (fail)
        {
            Check(shell.Message.Contains("factory failure") && shell.Message.Contains("Startup stage:"),
                "failure notification retains the original factory error and stage");
            return;
        }
        await TestRuntime.WaitIdle();
        Check(calls.All(x => x == 1), "all three registration callbacks run once even when their arrays are empty");
        Check(Rejected("models") && Rejected("mediators") && Rejected("bridges"),
            "empty model, mediator and bridge registrations cannot be repeated");
        Host.Instance.Report();
        await TestRuntime.WaitIdle();
        Check(updates >= 2 && completions == 2,
            "progress update and completion factories remain repeatable across independent roots");
        Check(typeof(RFacade).GetMethod("AbortAllCommands")!.IsStatic,
            "public abort-all is static and commands retain their bound cancellation helper");
    }

    private static bool Rejected(string which)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object registry = typeof(RFacade).GetField("actors", flags)!.GetValue(Host.Instance)!;
        try
        {
            if (which == "bridges")
                typeof(RFacade).GetMethod("InitialiseBridgesChild", flags)!.Invoke(Host.Instance, null);
            else
            {
                object factory = which == "models"
                    ? (object)(Func<IRModel[]>)(() => { Interlocked.Increment(ref repeatedFactoryCalls); return []; })
                    : (Func<RMediator[]>)(() => { Interlocked.Increment(ref repeatedFactoryCalls); return []; });
                registry.GetType().GetMethod(which == "models" ? "RegisterModels" : "RegisterMediators", flags)!
                    .Invoke(registry, [factory]);
            }
        }
        catch (TargetInvocationException error)
        {
            return error.InnerException is InvalidOperationException invalid
                && invalid.Message.Contains(typeof(Host).FullName!)
                && invalid.Message.Contains("registration has already been attempted");
        }
        return false;
    }

    private static void Factory(string which, int index)
    {
        Interlocked.Increment(ref calls[index]);
        if (which != category) return;
        entered.TrySetResult();
        if (!release.Wait(TimeSpan.FromSeconds(8))) throw new TimeoutException("Fixture registration gate timed out.");
        if (fail) throw new IOException("factory failure");
    }
    public sealed class Host : RFacade
    {
        internal static Host Instance = null!;
        public Host() => Instance = this;
        protected override IRModel[] RegisterModels() { Factory("models", 0); return []; }
        protected override RMediator[] RegisterMediators() { Factory("mediators", 1); return []; }
        protected override RBridge[] RegisterBridges() { Factory("bridges", 2); return []; }
        protected override RCommandBase CreateInitialiseCommand(IRInitialisation? payload) => new Init();
        internal void Report() => Execute(new Report(), new Report());
        protected override RCommandBase? CreateProgressUpdateCommand(string root, RProgress[] progress)
        { Interlocked.Increment(ref updates); return new Notice(); }
        protected override RCommandBase? CreateProgressCompleteCommand(string root)
        { Interlocked.Increment(ref completions); return new Notice(); }
    }
    private sealed class Init : RCommand<RVoid> { protected override Task<RVoid> Run() => Complete(); }
    private sealed class Report : RCommand<RVoid>
    {
        protected override async Task<RVoid> Run() { await SetProgress(10, "working"); return await Complete(); }
    }
    private sealed class Notice : RCommand<RVoid> { protected override Task<RVoid> Run() => Complete(); }
    private sealed class Shell : IRShell
    {
        internal readonly TaskCompletionSource<RApplicationState> Settled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal string Message = "";
        public void HandleStateChanged(RApplicationState state, string? message = null)
        {
            if (state is RApplicationState.Failed or RApplicationState.Running)
            { Message = message ?? ""; Settled.TrySetResult(state); }
        }
        public bool ShowMessageBox(string title, string message, bool isYesNo = false) => false;
    }
}
