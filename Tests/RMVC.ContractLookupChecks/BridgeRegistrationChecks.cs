using RMVC;

namespace RMVC.ContractLookupChecks;

internal static class BridgeRegistrationChecks
{
    private static string mode = "";
    private static int initialisers, cleanup;
    private static readonly List<string> cleaned = [];
    internal static async Task Run(string scenario)
    {
        mode = scenario;
        var shell = new Shell();
        RFacadeChildRegistration[] children = scenario switch
        {
            "missing" or "self" => [],
            "base" => [RFacade.Child<ActualChild>()],
            "sibling" or "reused" => [RFacade.Child<Requester>(), RFacade.Child<Sibling>()],
            _ => throw new ArgumentException(scenario)
        };
        RFacade.Create<Host>(shell, children);
        string message = await shell.Failed.Task.WaitAsync(TimeSpan.FromSeconds(8));
        Type owner = scenario is "sibling" or "reused" ? typeof(Requester) : typeof(Host);
        Type target = scenario switch
        {
            "self" => typeof(Host), "base" => typeof(ChildBase),
            "sibling" or "reused" => typeof(Sibling), _ => typeof(Missing)
        };
        Program.Check(message.Contains(owner.FullName!) && message.Contains(target.FullName!)
            && message.Contains("bridge") && message.Contains("registered direct child"),
            scenario + ": startup failure names registering facade, bridge and missing exact child");
        Program.Check(message.Contains("RegisterBridges") && message.Contains("remove the bridge registration"),
            scenario + ": diagnostics retain registration stage and actionable correction");
        Program.Check(initialisers == 0, scenario + ": no initialiser runs after invalid bridge registration");
        Program.Check(cleanup == 1 + children.Length, scenario + ": registered models are cleaned before Failed notification");
        Program.Check(cleaned.SequenceEqual(scenario switch
        {
            "base" => new[] { "actual", "host" },
            "sibling" or "reused" => new[] { "sibling", "requester", "host" },
            _ => new[] { "host" }
        }), scenario + ": failure cleanup preserves reverse facade order");
        if (scenario == "reused")
            Program.Check(Host.Shared != null,
                "reused: a bridge's prior binding cannot satisfy another facade's missing child");
    }

    private sealed class Owned(string name) : RModel
    {
        public override Task Cleanup() { cleanup++; cleaned.Add(name); return Task.CompletedTask; }
    }
    private sealed class Gateway<T> : RBridge<T> where T : RFacade { }
    private sealed class Host : RFacade
    {
        internal static Gateway<Sibling>? Shared;
        protected override IRModel[] RegisterModels() => [new Owned("host")];
        protected override RMediator[] RegisterMediators() => [];
        protected override RBridge[] RegisterBridges() => mode switch
        {
            "missing" => [new Gateway<Missing>()],
            "self" => [new Gateway<Host>()],
            "base" => [new Gateway<ChildBase>()],
            "reused" => [Shared = new Gateway<Sibling>()],
            _ => []
        };
        protected override RCommandBase CreateInitialiseCommand(IRInitialisation? payload) => new Init();
    }
    private abstract class ChildBase : RFacade { }
    private sealed class ActualChild : ChildBase
    {
        protected override IRModel[] RegisterModels() => [new Owned("actual")];
        protected override RMediator[] RegisterMediators() => [];
        protected override RCommandBase CreateInitialiseCommand(IRInitialisation? payload) => new Init();
    }
    private sealed class Requester : RFacade
    {
        protected override IRModel[] RegisterModels() => [new Owned("requester")];
        protected override RMediator[] RegisterMediators() => [];
        protected override RBridge[] RegisterBridges() => [mode == "reused" ? Host.Shared! : new Gateway<Sibling>()];
        protected override RCommandBase CreateInitialiseCommand(IRInitialisation? payload) => new Init();
    }
    private sealed class Sibling : RFacade
    {
        protected override IRModel[] RegisterModels() => [new Owned("sibling")];
        protected override RMediator[] RegisterMediators() => [];
        protected override RCommandBase CreateInitialiseCommand(IRInitialisation? payload) => new Init();
    }
    private sealed class Missing : RFacade
    {
        protected override IRModel[] RegisterModels() => [];
        protected override RMediator[] RegisterMediators() => [];
        protected override RCommandBase CreateInitialiseCommand(IRInitialisation? payload) => new Init();
    }
    private sealed class Init : RCommand<RVoid>
    {
        protected override Task<RVoid> Run() { initialisers++; return Complete(); }
    }
    private sealed class Shell : IRShell
    {
        internal readonly TaskCompletionSource<string> Failed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void HandleStateChanged(RApplicationState state, string? message = null)
        {
            if (state == RApplicationState.Failed) Failed.TrySetResult(message ?? "");
            if (state == RApplicationState.Running) Failed.TrySetException(new Exception("Invalid registration reached Running"));
        }
        public bool ShowMessageBox(string title, string message, bool isYesNo = false) => false;
    }
}
