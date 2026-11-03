using System.Reflection;
using RMVC;

namespace RMVC.ContractLookupChecks;

internal static class ConstructionCleanupChecks
{
    private static string scenario = "";
    private static readonly List<string> history = new();
    private static readonly object gate = new();
    private static readonly TaskCompletionSource entered = Signal(), release = Signal();
    private static readonly ItemA root = new("root"), child = new("child"), extra = new("extra");
    private static readonly ItemB last = new("last");
    private static int initialisers;
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Record(string value) { lock (gate) history.Add(value); }
    private static string[] History() { lock (gate) return history.ToArray(); }
    private static void Check(bool value, string message) => Program.Check(value, scenario + ": " + message);
    private static async Task Until(Func<bool> predicate)
    {
        var limit = DateTime.UtcNow.AddSeconds(10);
        while (!predicate() && DateTime.UtcNow < limit) await Task.Delay(5);
        if (!predicate()) throw new Exception("Timed out: " + scenario);
    }
    private static bool Reserved(Type type)
    {
        var registryGate = typeof(RFacade).GetField("registryGate", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        lock (registryGate)
            return ((HashSet<Type>)typeof(RFacade).GetField("pendingFacades", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!).Contains(type);
    }
    private static object? Owner(object value)
    {
        var registry = typeof(RFacade).Assembly.GetType("RMVC.RActorRegistry")!;
        var registryGate = registry.GetField("ownershipGate", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        lock (registryGate)
        {
            var owners = (Dictionary<object, RFacade>)registry.GetField("owners", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            return owners.TryGetValue(value, out var facade) ? facade : null;
        }
    }
    private static bool Rejected(Scope facade)
    {
        try { facade.Send(); return false; }
        catch (InvalidOperationException e) { return e.Message.Contains("startup failed"); }
    }

    internal static async Task Run(string mode)
    {
        scenario = mode;
        var shell = new Shell();
        if (mode == "invalid-call")
        {
            bool invalid = false;
            try { RFacade.Create<Root>(shell, null!); } catch (ArgumentNullException) { invalid = true; }
            Check(invalid && shell.Calls == 0 && !Reserved(typeof(Root)), "invalid first call throws without constructing or notifying");
            Check(Program.ThrowsLookup(() => RFacade.Create<Root>(shell), "only once"), "invalid first call still consumes creation eligibility");
            return;
        }
        // Intentionally no catch: assembly failures must no longer escape Create.
        RFacade.Create<Root>(shell, RFacade.Child<Child>());
        if (mode == "delayed")
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Check(shell.Calls == 0 && Reserved(typeof(Root)) && Reserved(typeof(Child)), "notification and type release await cleanup");
            Check(ReferenceEquals(Owner(last), Child.Instance), "model ownership remains bound during cleanup");
            Check(Rejected(Root.Instance) && Rejected(Child.Instance), "partially assembled facades reject all later roots");
            bool rejected = false;
            try { RFacade.Create<Root>(shell); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected && shell.Calls == 0, "overlapping Create throws immediately without replacing the failure recipient");
            Check(Program.ThrowsLookup(() => RFacade.Create<Contender>(new Shell()), "only once")
                && !History().Contains("end:last"), "a different root cannot bypass the pending cleanup claim");
            release.TrySetResult();
        }
        await shell.Failed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Until(() => !Reserved(typeof(Root)) && !Reserved(typeof(Child)));
        Check(initialisers == 0, "failed composition never starts its initialisers");
        string[] expected = mode switch
        {
            "root-constructor" => [],
            "child-constructor" or "models-callback" or "null-array" => ["root"],
            "duplicate-model" => ["extra", "child", "root"],
            "repeated-instance" or "null-entry" => ["child", "root"],
            _ => ["last", "child", "root"]
        };
        Check(History().Where(x => x.StartsWith("start:")).Select(x => x[6..]).SequenceEqual(expected), "every received eligible model cleans once in reverse order");
        Check(!History().Contains("ordinary-root"), "queued ordinary work is discarded");
        Check(shell.Calls == 1 && shell.Message.Contains("Startup stage:"), "one callback includes the failing startup stage");
        string expectedFacade = mode == "root-constructor" ? typeof(Root).FullName! : typeof(Child).FullName!;
        Check(shell.Facade == expectedFacade, "callback identifies the failing facade, including constructor failures");
        if (mode is "root-constructor" or "child-constructor")
            Check(shell.Message.Contains("constructor failed"), "constructor diagnostic preserves the original exception message");
    
        if (mode == "cleanup-errors")
        {
            Check(shell.Message.Contains("cleanup exploded") && History().Contains("end:root"), "cleanup failure is reported and later models still clean up");
            Check(Owner(child) != null, "failed cleanup retains unpublished ownership rather than enabling unsafe reuse");
        }
        else Check(expected.All(name => Owner(name == "root" ? root : name == "child" ? child : name == "last" ? last : extra) == null), "successful cleanup releases unpublished claims before notification");

    }

    public abstract class Item(string name) : RModel
    {
        public override async Task Cleanup()
        {
            Record("start:" + name);
            if (scenario == "delayed" && name == "last") { entered.TrySetResult(); await release.Task; }
            else await Task.Delay(1);
            Record("end:" + name);
            if (scenario == "cleanup-errors" && name == "child") throw new IOException("cleanup exploded");
        }
    }
    public sealed class ItemA(string name) : Item(name) { }
    public sealed class ItemB(string name) : Item(name) { }
    public abstract class Scope : RFacade
    {
        internal void Send() => Execute(new Probe("ordinary-root"));
        protected override RCommandBase CreateInitialiseCommand(IRInitialisation? payload) => new Initialise();
        protected override RMediator[] RegisterMediators() => [];
    }
    public sealed class Root : Scope
    {
        internal static Root Instance = null!;
        public Root() { Instance = this; if (scenario == "root-constructor") throw new IOException("constructor failed"); }
        protected override IRModel[] RegisterModels() { Send(); return [root]; }
    }
    public sealed class Child : Scope
    {
        internal static Child Instance = null!;
        public Child() { Instance = this; if (scenario == "child-constructor") throw new IOException("constructor failed"); }
        protected override IRModel[] RegisterModels() => scenario switch
        {
            "models-callback" => throw new IOException("models callback failed"),
            "null-array" => null!,
            "duplicate-model" => [child, extra],
            "repeated-instance" => [child, child],
            "null-entry" => [child, null!],
            _ => [child, last]
        };
        protected override RMediator[] RegisterMediators() => scenario switch
        {
            "mediators-callback" => throw new IOException("mediators callback failed"),
            "null-mediators" => null!,
            "duplicate-mediator" => [new Presenter(), new Presenter()],
            _ => []
        };
        protected override RBridge[] RegisterBridges() => scenario switch
        {
            "null-bridges" => null!,
            "null-bridge" => [null!],
            _ => throw new IOException("bridges callback failed")
        };
    }
    public sealed class Contender : Scope { protected override IRModel[] RegisterModels() => [last]; }
    private sealed class Initialise : RCommand<RVoid>
    {
        protected override Task<RVoid> Run() { Interlocked.Increment(ref initialisers); return Complete(); }
    }
    private sealed class Probe(string value) : RCommand<RVoid>
    {
        protected override Task<RVoid> Run() { Record(value); return Complete(); }
    }
    public interface IView : IRContract { }
    public sealed class Presenter : RMediator<IView>
    {
        protected override void ActorAttached(IView actor) { }
        protected override void ActorDetached(IView actor) { }
    }
    private sealed class Shell : IRShell
    {
        internal readonly TaskCompletionSource Failed = Signal();
        internal int Calls;
        internal string Message = "", Facade = "";
        public void HandleStateChanged(RMVC.RApplicationState state, string? detail = null)
        {
            if (state != RMVC.RApplicationState.Failed) return;
            string message = detail ?? "";
            string facadeName = message.StartsWith("Facade: ") ? message.Split('\n')[0][8..].Trim() : "";
            if (message.StartsWith("Facade: ")) message = message[(message.IndexOf('\n') + 1)..];
Facade = facadeName; Message = message; Calls++; Failed.TrySetResult();
            if (scenario == "throwing-shell") throw new IOException("shell callback failed");
        }
        public bool ShowMessageBox(string title, string message, bool isYesNo = false) => false;
    }
}
