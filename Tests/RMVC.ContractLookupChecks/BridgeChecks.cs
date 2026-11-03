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
            new UnboundProbe().Verify();
            RFacade.Create<Host>(new Shell(),
                RFacade.Child<Destination>(), RFacade.Child<OtherDestination>());

            await finished.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }

        private sealed class UnboundProbe : RCommand<RVoid>
        {
            internal void Verify()
            {
                Program.Check(Program.ThrowsLookup(() => FacadeChild<Destination>(), "not bound", typeof(Destination).FullName!),
                    "required child lookup rejects unbound commands and names the requested type");
                Program.Check(Program.ThrowsLookup(() => BridgeChild<Gateway<Destination>>(), "not bound", typeof(Gateway<Destination>).FullName!),
                    "required bridge lookup rejects unbound commands and names the requested type");
                Program.Check(typeof(RCommandBase).GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .All(method => method.Name is not "FacadeChildOrNull" and not "BridgeChildOrNull"),
                    "child and bridge lookup expose no optional variants");
                Program.Check(FacadeParentByContract<IHostCapability>() == null,
                    "valid optional parent contract remains null before binding");
                Program.Check(Program.RejectsClassContract(() => FacadeParentByContract<Host>(), typeof(Host)),
                    "invalid parent class contract throws even before binding");
            }
            protected override Task<RVoid> Run() => Complete();
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

            protected override RCommandBase CreateInitialiseCommand(IRInitialisation? initialisation)
                => new IncrementCmd();
        }

        public sealed class Host : HostBase, IHostCapability
        {
            protected override RMediator[] RegisterMediators() => [];

            internal static readonly Counter State = new();

            public void RequestHostOperation() => Execute(new HostOperationCmd());

            protected override IRModel[] RegisterModels() => [State];

            protected override RBridge[] RegisterBridges()
                => [new Gateway<Destination>(), new Gateway<OtherDestination>()];

            protected override RCommandBase CreateInitialiseCommand(IRInitialisation? initialisation)
                => new StartCmd();
        }

        public sealed class Destination : RFacade, IUniqueChild, ISharedChild
        {
            protected override RMediator[] RegisterMediators() => [];

            internal static readonly Counter State = new();
            internal static bool Initialised;

            protected override IRModel[] RegisterModels() => [State];

            protected override RCommandBase CreateInitialiseCommand(IRInitialisation? initialisation)
                => new InitialiseDestinationCmd();
        }

        public sealed class OtherDestination : RFacade, ISharedChild
        {
            protected override RMediator[] RegisterMediators() => [];

            internal static readonly Counter State = new();

            protected override IRModel[] RegisterModels() => [State];

            protected override RCommandBase CreateInitialiseCommand(IRInitialisation? initialisation)
                => new NoOpCmd();
        }

        private sealed class InitialiseDestinationCmd : RCommand<RVoid>
        {
            protected override Task<RVoid> Run()
            {
                Program.Check(FacadeParentByContract<IHostCapability>() is Host
                    && Program.RejectsClassContract(() => FacadeParentByContract<Host>(), typeof(Host))
                    && Program.RejectsClassContract(() => FacadeParentByContract<HostBase>(), typeof(HostBase)),
                    "parent lookup accepts interface contracts and rejects concrete/base facade classes");
                Program.Check(Program.ThrowsLookup(() => FacadeChild<OtherDestination>(), "direct child", typeof(OtherDestination).FullName!)
                    && FacadeParentByContract<ISharedChild>() == null,
                    "child lookups cannot discover a sibling or use one as a parent fallback");

                FacadeParentByContract<IHostCapability>()!.RequestHostOperation();
                Destination.Initialised = true;
                return Complete();
            }
        }

        private sealed class HostOperationCmd : RCommand<RVoid>
        {
            protected override async Task<RVoid> Run()
            {
                try
                {
                    await executed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    Program.Check(Destination.Initialised && Destination.State.Value == 1
                        && OtherDestination.State.Value == 1 && Host.State.Value == 0,
                        "multiple gateways execute against their exact destination after initialisation");
                    Program.Check(ReferenceEquals(Model<Counter>(), Host.State),
                        "parent facade capability dispatch creates a root in the parent's registry");
                    finished.TrySetResult();
                }
                catch (Exception error)
                {
                    finished.TrySetException(error);
                }

                return await Complete();
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
                Model<Counter>().Value++;

                if (Interlocked.Increment(ref executionCount) == 2)
                    executed.TrySetResult();

                return Complete();
            }
        }

        private sealed class StartCmd : RCommand<RVoid>
        {
            protected override Task<RVoid> Run()
            {
                try
                {
                    Program.Check(FacadeParentByContract<IRFacade>() == null,
                        "a composition root has no implicit parent");
                    Program.Check(FacadeChild<Destination>() is Destination
                        && Program.ThrowsLookup(() => FacadeChild<RFacade>(), "exact type", typeof(RFacade).FullName!)
                        && Program.ThrowsLookup(() => FacadeChild<Host>(), "direct child", typeof(Host).FullName!),
                        "child lookup matches exact concrete types without base-class or self fallback");

                    bool interfaceRejected = false;

                    try
                    {
                        typeof(RCommandBase).GetMethod("FacadeChild",
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

                    Program.Check(Program.ThrowsLookup(() => BridgeChild<RBridge>(), "exact type", typeof(RBridge).FullName!),
                        "bridge lookup does not fall back to a registered derived bridge");
                    var first = BridgeChild<Gateway<Destination>>();
                    Program.Check(ReferenceEquals(first, BridgeChild<Gateway<Destination>>())
                        && Gateway<Destination>.Instances == 1,
                        "bridge lookup retains the registered instance");

                    first.Increment();
                    BridgeChild<Gateway<OtherDestination>>().Increment();

                    Program.Check(Destination.State.Value == 0 && !Destination.Initialised
                        && OtherDestination.State.Value == 0 && Host.State.Value == 0,
                        "early bridge roots queue on their destinations without running in the host");

                    InitialiseFacadeChild<Destination>();
                    InitialiseFacadeChild<OtherDestination>();
                    Program.Check(Program.ThrowsLookup(() => BridgeChild<Gateway<Host>>(), "RegisterBridges", typeof(Gateway<Host>).FullName!),
                        "unregistered required bridge lookup throws and explains registration");
                }
                catch (Exception error)
                {
                    finished.TrySetException(error);
                }

                return Complete();
            }
        }
    }
}
