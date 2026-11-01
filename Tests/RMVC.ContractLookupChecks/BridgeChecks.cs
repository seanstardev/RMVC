using RMVC;

namespace RMVC.ContractLookupChecks
{
    internal static class BridgeChecks
    {
        private static readonly TaskCompletionSource finished = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        private static readonly TaskCompletionSource executed = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        private static int executionCount;

        internal static async Task Run()
        {
            RFacade.Create<Host>(new Shell(),
                RFacade.Child<Destination>(), RFacade.Child<OtherDestination>());

            await finished.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }

        public interface IHostCapability : IRFacade
        {
            void RequestHostOperation();
        }

        public interface IUniqueChild : IRFacade { }

        public interface ISharedChild : IRFacade { }

        public abstract class HostBase : RFacade { }

        public sealed class Counter : RModel
        {
            public int Value { get; set; }
        }

        public sealed class Gateway<TFacade> : RBridge<TFacade>
            where TFacade : RFacade
        {
            public static int Instances;

            public Gateway() => Instances++;

            public void Increment() => Execute(new IncrementCmd());
        }

        public sealed class MissingDestination : RFacade
        {
            protected override IRModel[] RegisterModels() => [];

            protected override RMediator[] RegisterMediators() => [];

            protected override RCommandBase CreateInitialiseCommand(object? initialisation)
                => new IncrementCmd();
        }

        public sealed class Host : HostBase, IHostCapability
        {
            protected override RMediator[] RegisterMediators() => [];

            internal static readonly Counter State = new();

            public void RequestHostOperation() => Execute(new HostOperationCmd());

            protected override IRModel[] RegisterModels() => [State];

            protected override RBridge[] RegisterBridges()
                => [new Gateway<Destination>(), new Gateway<OtherDestination>(),
                    new Gateway<MissingDestination>()];

            protected override RCommandBase CreateInitialiseCommand(object? initialisation)
                => new StartCmd();
        }

        public sealed class Destination : RFacade, IUniqueChild, ISharedChild
        {
            protected override RMediator[] RegisterMediators() => [];

            internal static readonly Counter State = new();
            internal static bool Initialised;

            protected override IRModel[] RegisterModels() => [State];

            protected override RCommandBase CreateInitialiseCommand(object? initialisation)
                => new InitialiseDestinationCmd();
        }

        public sealed class OtherDestination : RFacade, ISharedChild
        {
            protected override RMediator[] RegisterMediators() => [];

            internal static readonly Counter State = new();

            protected override IRModel[] RegisterModels() => [State];

            protected override RCommandBase CreateInitialiseCommand(object? initialisation)
                => new NoOpCmd();
        }

        private sealed class InitialiseDestinationCmd : RCommand<RVoid>
        {
            protected override Task<RVoid> Run()
            {
                Program.Check(GetFacadeParentByContract<IHostCapability>() is Host
                    && GetFacadeParentByContract<Host>() == null
                    && GetFacadeParentByContract<HostBase>() == null,
                    "parent lookup accepts interface contracts and rejects concrete/base facade classes");
                Program.Check(GetFacadeChild<OtherDestination>() == null
                    && GetFacadeParentByContract<ISharedChild>() == null,
                    "child lookups cannot discover a sibling or use one as a parent fallback");

                GetFacadeParentByContract<IHostCapability>()!.RequestHostOperation();
                Destination.Initialised = true;
                return Complete();
            }
        }

        private sealed class HostOperationCmd : RCommand<RVoid>
        {
            protected override Task<RVoid> Run()
            {
                try
                {
                    Program.Check(ReferenceEquals(Model<Counter>(), Host.State),
                        "parent facade capability dispatch creates a root in the parent's registry");
                    finished.TrySetResult();
                }
                catch (Exception error)
                {
                    finished.TrySetException(error);
                }

                return Complete();
            }
        }

        private sealed class NoOpCmd : RCommand<RVoid>
        {
            protected override Task<RVoid> Run() => Complete();
        }

        private sealed class IncrementCmd : RCommand<RVoid>
        {
            protected override Task<RVoid> Run()
            {
                Model<Counter>()!.Value++;

                if (Interlocked.Increment(ref executionCount) == 2)
                    executed.TrySetResult();

                return Complete();
            }
        }

        private sealed class StartCmd : RCommand<RVoid>
        {
            protected override async Task<RVoid> Run()
            {
                try
                {
                    Program.Check(GetFacadeParentByContract<IRFacade>() == null,
                        "a composition root has no implicit parent");
                    Program.Check(GetFacadeChild<Destination>() is Destination
                        && GetFacadeChild<RFacade>() == null
                        && GetFacadeChild<Host>() == null,
                        "child lookup matches exact concrete types without base-class or self fallback");

                    bool interfaceRejected = false;

                    try
                    {
                        typeof(RCommandBase).GetMethod("GetFacadeChild",
                            System.Reflection.BindingFlags.Instance
                                | System.Reflection.BindingFlags.NonPublic)!
                            .MakeGenericMethod(typeof(IUniqueChild));
                    }
                    catch (ArgumentException)
                    {
                        interfaceRejected = true;
                    }

                    Program.Check(interfaceRejected,
                        "child lookup's generic constraint excludes facade interfaces");

                    var first = ChildBridge<Gateway<Destination>>();
                    Program.Check(first != null
                        && ReferenceEquals(first, ChildBridge<Gateway<Destination>>())
                        && Gateway<Destination>.Instances == 1,
                        "bridge lookup retains the registered instance");

                    first!.Increment();
                    ChildBridge<Gateway<OtherDestination>>()!.Increment();
                    ChildBridge<Gateway<MissingDestination>>()!.Increment();

                    Program.Check(Destination.State.Value == 0 && !Destination.Initialised
                        && OtherDestination.State.Value == 0 && Host.State.Value == 0,
                        "early bridge roots queue on their destinations without running in the host");

                    await GetFacadeChild<Destination>()!.Initialise(null);
                    await GetFacadeChild<OtherDestination>()!.Initialise(null);
                    await executed.Task.WaitAsync(TimeSpan.FromSeconds(5));

                    Program.Check(Destination.Initialised && Destination.State.Value == 1
                        && OtherDestination.State.Value == 1 && Host.State.Value == 0,
                        "multiple gateways execute against their exact destination after initialisation");
                    Program.Check(ChildBridge<Gateway<Host>>() == null,
                        "unregistered bridge lookup returns null");
                }
                catch (Exception error)
                {
                    finished.TrySetException(error);
                }

                return await Complete();
            }
        }
    }
}
