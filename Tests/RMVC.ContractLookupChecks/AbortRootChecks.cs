using RMVC;

namespace RMVC.ContractLookupChecks;

internal static class AbortRootChecks
{
    private static readonly TaskCompletionSource Release = NewSignal();

    internal static async Task Run()
    {
        RFacade.Create<Host>(new Shell(), RFacade.Child<Peer>());
        await TestRuntime.WaitIdle();

        var matchedChild = new Target();
        var first = new Target(matchedChild);
        var second = new Target();
        var childOnly = new Target();
        var unrelated = new TargetContainer(childOnly);
        var derived = new DerivedTarget();
        var otherFacade = new Target();
        Host.Instance.Submit(first);
        Host.Instance.Submit(second);
        Host.Instance.Submit(unrelated);
        Host.Instance.Submit(derived);
        Peer.Instance.Submit(otherFacade);

        var cancel = new CancelTarget();
        try
        {
            await Task.WhenAll(matchedChild.Entered.Task, second.Entered.Task,
                childOnly.Entered.Task, derived.Entered.Task, otherFacade.Entered.Task)
                .WaitAsync(TimeSpan.FromSeconds(5));
            Host.Instance.Submit(cancel);
            await cancel.Requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            Release.TrySetResult();
        }

        await TestRuntime.WaitIdle();
        Program.Check(first.Aborted && second.Aborted && matchedChild.Aborted,
            "targeted abort cancels all matching roots and their children");
        Program.Check(!childOnly.Aborted && !derived.Aborted && !otherFacade.Aborted,
            "targeted abort leaves a child-only match, a derived root and another facade alone");

        var later = new Target();
        Host.Instance.Submit(later);
        await later.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await TestRuntime.WaitIdle();
        Program.Check(!later.Aborted, "targeted abort keeps admission open for a later matching root");
    }

    private static TaskCompletionSource NewSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public sealed class Host : RFacade
    {
        internal static Host Instance { get; private set; } = null!;
        protected override IRModel[] RegisterModels() => [];
        protected override RMediator[] RegisterMediators() => [];
        protected override RCommandBase CreateInitialiseCommand(IRInitialisation? initialisation)
        {
            Instance = this;
            return new InitialiseHost();
        }
        internal void Submit(RCommandBase command) => Execute(command);
    }

    public sealed class Peer : RFacade
    {
        internal static Peer Instance { get; private set; } = null!;
        protected override IRModel[] RegisterModels() => [];
        protected override RMediator[] RegisterMediators() => [];
        protected override RCommandBase CreateInitialiseCommand(IRInitialisation? initialisation)
        {
            Instance = this;
            return new InitialisePeer();
        }
        internal void Submit(RCommandBase command) => Execute(command);
    }

    private sealed class InitialiseHost : RCommand<RVoid>
    {
        protected override Task<RVoid> Run()
        {
            InitialiseFacadeChild<Peer>();
            return Complete();
        }
    }

    private sealed class InitialisePeer : RCommand<RVoid>
    {
        protected override Task<RVoid> Run() => Complete();
    }

    private class Target(Target? child = null) : RCommand<RVoid>
    {
        internal TaskCompletionSource Entered { get; } = NewSignal();
        internal bool Aborted => AbortRequested;

        protected override async Task<RVoid> Run()
        {
            Entered.TrySetResult();
            if (child != null)
                await Execute(child);

            await Release.Task;
            return await Complete();
        }
    }

    private sealed class DerivedTarget : Target { }

    private sealed class TargetContainer(Target child) : RCommand<RVoid>
    {
        protected override Task<RVoid> Run() => Execute(child);
    }

    private sealed class CancelTarget : RCommand<RVoid>
    {
        internal TaskCompletionSource Requested { get; } = NewSignal();

        protected override Task<RVoid> Run()
        {
            AbortRootCommands<Target>();
            Requested.TrySetResult();
            return Complete();
        }
    }
}
