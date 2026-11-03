using RMVC;

namespace RMVC.ContractLookupChecks;

internal static class ExecutionGuardChecks
{
    private static int childRuns, rootRuns;
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static readonly TaskCompletionSource childrenDone = Signal();
    private static void Check(bool condition, string message) => Program.Check(condition, message);

    private static bool Rejects(Action action, Type origin, string reason)
    {
        try { action(); }
        catch (InvalidOperationException error)
        {
            return error.Message.Contains(origin.FullName!) && error.Message.Contains(reason);
        }
        return false;
    }

    internal static async Task Run()
    {
        var first = new ValueCmd(23);
        var second = new ValueCmd(41);
        Check(Rejects(() => _ = new UntypedConstructor(first), typeof(UntypedConstructor), "constructor"),
            "untyped child Execute throws synchronously from a command constructor");
        Check(Rejects(() => _ = new TypedConstructor(second), typeof(TypedConstructor), "constructor"),
            "typed child Execute throws synchronously instead of returning a default result");
        var unbound = new Parent(first, second);
        Check(Rejects(() => _ = unbound.SubmitUntyped(first), typeof(Parent), "bound for execution"),
            "an unbound command's untyped execution entry point throws before returning a Task");
        Check(Rejects(() => _ = unbound.SubmitTyped(second), typeof(Parent), "bound for execution"),
            "an unbound command's typed execution entry point throws before returning a Task");
        Check(childRuns == 0, "rejected unbound calls do not run children");

        var model = new Sender();
        var mediator = new Presenter();
        var orphanRoot = new RootCmd();
        Check(Rejects(() => model.Send(orphanRoot), typeof(Sender), "facade binding"),
            "an unregistered model rejects root submission immediately");
        Check(Rejects(() => mediator.Send(orphanRoot), typeof(Presenter), "facade binding"),
            "an unregistered mediator rejects root submission immediately");
        Check(Rejects(() => _ = new ConstructorModel(), typeof(ConstructorModel), "constructor"),
            "a model constructor cannot silently submit roots before registration");
        Check(Rejects(() => _ = new ConstructorMediator(), typeof(ConstructorMediator), "constructor"),
            "a mediator constructor cannot silently submit roots before registration");

        Check(Rejects(() => new Gateway<MissingChild>().Send(orphanRoot),
            typeof(Gateway<MissingChild>), "facade binding"),
            "an unregistered bridge rejects root submission immediately");

        RFacade.Create<Host>(new Shell());
        await TestRuntime.WaitIdle();
        Host.Instance.Send(unbound);
        await childrenDone.Task.WaitAsync(TimeSpan.FromSeconds(8));
        await TestRuntime.WaitIdle();
        Check(childRuns == 2, "rejected child submissions did not reserve instances; both later execute normally");

        Host.State.Send(orphanRoot);
        await TestRuntime.WaitIdle();
        Check(rootRuns == 1, "rejected model/mediator submissions did not reserve the root instance");
        Host.View.Send(new RootCmd());
        await TestRuntime.WaitIdle();
        Check(rootRuns == 2, "a registered mediator can submit roots without an attached actor");
        Check(Rejects(() => model.Send(new RootCmd()), typeof(Sender), "facade binding"),
            "creating the application does not bind an unregistered instance to its facade");
        Check(rootRuns == 2, "rejected unbound bridge submission did not run or reserve the root");
    }

    private sealed class ValueCmd(int value) : RCommand<int>
    {
        protected override Task<int> Run() { childRuns++; return Complete(value); }
    }
    private sealed class UntypedConstructor : RCommand<RVoid>
    {
        internal UntypedConstructor(RCommandBase child) { _ = Execute(child); }
        protected override Task<RVoid> Run() => Complete();
    }
    private sealed class TypedConstructor : RCommand<RVoid>
    {
        internal TypedConstructor(RCommand<int> child) { _ = Execute(child); }
        protected override Task<RVoid> Run() => Complete();
    }
    private sealed class Parent(ValueCmd first, ValueCmd second) : RCommand<RVoid>
    {
        internal Task SubmitUntyped(RCommandBase child) => Execute(child);
        internal Task<int> SubmitTyped(RCommand<int> child) => Execute(child);
        protected override async Task<RVoid> Run()
        {
            Check(RejectsNull(() => _ = Execute((RCommandBase)null!)),
                "bound untyped execution rejects a null child before reservation");
            Check(RejectsNull(() => _ = Execute((RCommand<int>)null!)),
                "bound typed execution rejects a null child before reservation");
            await Execute((RCommandBase)first);
            int result = await Execute(second);
            Check(result == 41, "valid nested typed execution preserves its result");
            childrenDone.TrySetResult();
            return await Complete();
        }
        private static bool RejectsNull(Action action)
        {
            try { action(); } catch (ArgumentNullException) { return true; }
            return false;
        }
    }
    private sealed class RootCmd : RCommand<RVoid>
    {
        protected override Task<RVoid> Run() { rootRuns++; return Complete(); }
    }
    private class Sender : RModel { internal void Send(params RCommandBase[] commands) => Execute(commands); }
    private interface IView : IRContract { }
    private class Presenter : RMediator<IView>
    {
        internal void Send(params RCommandBase[] commands) => Execute(commands);
        protected override void ActorAttached(IView actor) { }
        protected override void ActorDetached(IView actor) { }
    }
    private sealed class ConstructorModel : Sender { internal ConstructorModel() { Send(new RootCmd()); } }
    private sealed class ConstructorMediator : Presenter { internal ConstructorMediator() { Send(new RootCmd()); } }
    private sealed class Gateway<TFacade> : RBridge<TFacade> where TFacade : RFacade
    {
        internal void Send(params RCommandBase[] commands) => Execute(commands);
    }
    private sealed class MissingChild : RFacade
    {
        protected override IRModel[] RegisterModels() => [];
        protected override RMediator[] RegisterMediators() => [];
        protected override RCommandBase CreateInitialiseCommand(IRInitialisation? initialisation) => new Init();
    }
    private sealed class Host : RFacade
    {
        internal static Host Instance = null!;
        internal static readonly Sender State = new();
        internal static readonly Presenter View = new();
        public Host() { Instance = this; }
        internal void Send(RCommandBase command) => Execute(command);
        protected override IRModel[] RegisterModels() => [State];
        protected override RMediator[] RegisterMediators() => [View];
        protected override RBridge[] RegisterBridges() => [];
        protected override RCommandBase CreateInitialiseCommand(IRInitialisation? initialisation) => new Init();
    }
    private sealed class Init : RCommand<RVoid> { protected override Task<RVoid> Run() => Complete(); }
    private sealed class Shell : IRShell
    {
        public bool ShowMessageBox(string title, string message, bool isYesNo = false) => false;
        public void HandleStateChanged(RApplicationState state, string? message = null)
        {
            if (state == RApplicationState.Failed) childrenDone.TrySetException(new Exception(message));
        }
    }
}
