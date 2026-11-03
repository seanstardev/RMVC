using System.Reflection;
using RMVC;

namespace RMVC.ContractLookupChecks;

internal static class CreationLifetimeChecks
{
    private static string scenario = "";
    private static int constructors, rejections, cleanups;
    private static readonly TaskCompletionSource entered = Signal(), release = Signal(), ran = Signal();
    private static readonly Shell shell = new();
    private static readonly ManualResetEventSlim constructorRelease = new(false);
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Check(bool value, string text) => Program.Check(value, scenario + ": " + text);
    private static bool Reject(Action action)
    {
        try { action(); return false; }
        catch (InvalidOperationException error)
        {
            return error.Message.Contains("only once") && error.Message.Contains("first attempt", StringComparison.OrdinalIgnoreCase);
        }
    }
    private static void Reenter()
    {
        if (Reject(() => RFacade.Create<Second>(new Shell()))) Interlocked.Increment(ref rejections);
    }
    internal static async Task Run(string mode)
    {
        scenario = mode;
        if (mode.StartsWith("invalid-"))
        {
            bool invalid = false;
            try
            {
                if (mode == "invalid-array") RFacade.Create<First>(shell, null!);
                else if (mode == "invalid-entry") RFacade.Create<First>(shell, [null!]);
                else RFacade.Create<First>(shell, RFacade.Child<First>());
            }
            catch (ArgumentException) { invalid = true; }
            Check(invalid && constructors == 0 && shell.Calls == 0, "invalid first call throws before application code");
            Check(Reject(() => RFacade.Create<Second>(shell)), "invalid first call permanently consumes creation eligibility");
            return;
        }
        if (mode == "concurrent")
        {
            using var barrier = new Barrier(2);
            Exception? Attempt(Action create)
            {
                barrier.SignalAndWait(TimeSpan.FromSeconds(8));
                try { create(); return null; } catch (Exception e) { return e; }
            }
            var a = Task.Run(() => Attempt(() => RFacade.Create<First>(shell)));
            var b = Task.Run(() => Attempt(() => RFacade.Create<Second>(shell)));
            var errors = await Task.WhenAll(a, b).WaitAsync(TimeSpan.FromSeconds(10));
            Check(constructors == 1 && errors.Count(x => x == null) == 1
                && errors.Count(x => x is InvalidOperationException && x.Message.Contains("only once")) == 1,
                "exactly one concurrent root wins before either application constructor can run twice");
            await ran.Task.WaitAsync(TimeSpan.FromSeconds(10));
            return;
        }
        if (mode == "pending-constructor")
        {
            var creation = Task.Run(() => RFacade.Create<First>(shell));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Check(Reject(() => RFacade.Create<Second>(new Shell())) && constructors == 1,
                "another thread cannot create a root while the first constructor is still running");
            constructorRelease.Set();
            await creation.WaitAsync(TimeSpan.FromSeconds(10));
            await ran.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Check(ReferenceEquals(TestRuntime.ReadShell(), shell), "the winning shell publishes normally after the rejected race");
            return;
        }
        RFacade.Create<First>(shell);
        if (mode == "during-construction")
        {
            // This mode uses the reentrant constructor check below; no blocking UI thread is needed.
            Check(rejections == 1, "constructor cannot reenter Create");
        }
        if (mode == "during-cleanup")
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Check(Reject(() => RFacade.Create<Second>(new Shell())) && cleanups == 0,
                "a different root is rejected while failed-startup cleanup is pending");
            release.TrySetResult();
        }
        if (mode is "constructor-failure" or "registration-failure" or "initialisation-failure" or "during-cleanup" or "notification-reentry")
        {
            await shell.Failed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Check(Reject(() => RFacade.Create<First>(shell)) && Reject(() => RFacade.Create<Second>(new Shell())),
                "failure and completed cleanup never permit recreation with any type or shell");
            Check(constructors == 1 && shell.Calls == 1, "rejected attempts do not construct facades or send another failure callback");
            if (mode == "notification-reentry") Check(rejections == 1, "failure notification cannot recreate RMVC");
            return;
        }
        await ran.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Check(Reject(() => RFacade.Create<First>(shell)), "same root and shell cannot be created twice");
        Check(Reject(() => RFacade.Create<Second>(shell)) && Reject(() => RFacade.Create<Second>(new Shell())),
            "different root types and shell instances cannot start another environment");
        Check(constructors == 1 && ReferenceEquals(TestRuntime.ReadShell(), shell),
            "rejection leaves the original shell and facade binding intact");
        int before = First.Instance.Probes;
        First.Instance.Send();
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (First.Instance.Probes == before && DateTime.UtcNow < deadline) await Task.Delay(5);
        Check(First.Instance.Probes > before, "the valid original environment still executes commands after rejected creation");
        if (mode == "initialisation-reentry") Check(rejections == 1, "initialisation cannot create another root");
        Check(!typeof(RFacade).GetMethods(BindingFlags.Public | BindingFlags.Static).Any(m => m.Name == "Reset"),
            "no public reset mechanism is introduced");
    }
    public abstract class Scope : RFacade
    {
        internal int Probes;
        internal void Send() => Execute(new Probe(this));
        protected override IRModel[] RegisterModels()
        {
            if (scenario == "registration-failure") throw new IOException("registration failed");
            return [new Resource()];
        }
        protected override RMediator[] RegisterMediators() => [];
        protected override RCommandBase CreateInitialiseCommand(IRInitialisation? initialisation) => new Initialise(this);
    }
    public sealed class First : Scope
    {
        internal static First Instance = null!;
        public First()
        {
            Instance = this; Interlocked.Increment(ref constructors);
            if (scenario == "pending-constructor")
            {
                entered.TrySetResult();
                if (!constructorRelease.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Constructor gate timed out.");
            }
            if (scenario == "during-construction") Reenter();
            if (scenario == "constructor-failure") throw new IOException("constructor failed");
        }
    }
    public sealed class Second : Scope { public Second() => Interlocked.Increment(ref constructors); }
    private sealed class Initialise(Scope owner) : RCommand<RVoid>
    {
        protected override async Task<RVoid> Run()
        {
            if (scenario == "initialisation-reentry") Reenter();
            await Task.Yield();
            if (scenario is "initialisation-failure" or "during-cleanup" or "notification-reentry") throw new IOException("initialisation failed");
            owner.Send();
            return await Complete();
        }
    }
    private sealed class Probe(Scope owner) : RCommand<RVoid>
    {
        protected override Task<RVoid> Run() { Interlocked.Increment(ref owner.Probes); ran.TrySetResult(); return Complete(); }
    }
    public sealed class Resource : RModel
    {
        public override async Task Cleanup()
        {
            if (scenario == "during-cleanup") { entered.TrySetResult(); await release.Task; }
            Interlocked.Increment(ref cleanups);
        }
    }
    private sealed class Shell : IRShell
    {
        internal readonly TaskCompletionSource Failed = Signal();
        internal int Calls;
        public void HandleStateChanged(RMVC.RApplicationState state, string? detail = null)
        {
            if (state != RMVC.RApplicationState.Failed) return;
            string message = detail ?? "";
            string name = message.StartsWith("Facade: ") ? message.Split('\n')[0][8..].Trim() : "";
            if (message.StartsWith("Facade: ")) message = message[(message.IndexOf('\n') + 1)..];
Calls++;
            if (scenario == "notification-reentry") Reenter();
            Failed.TrySetResult();
        }
        public bool ShowMessageBox(string title, string message, bool isYesNo = false) => false;
    }
}
