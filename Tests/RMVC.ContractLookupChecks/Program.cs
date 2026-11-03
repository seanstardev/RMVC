using System.Reflection;
using RMVC;

namespace RMVC.ContractLookupChecks
{
    internal static class Program
    {
        internal static readonly TaskCompletionSource Finished = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        internal static void Check(bool passed, string description)
        {
            if (!passed)
                throw new Exception(description);

            Console.WriteLine("PASS " + description);
        }

        internal static bool ThrowsLookup(Action action, params string[] fragments)
        {
            try { action(); }
            catch (InvalidOperationException error)
            {
                return fragments.All(error.Message.Contains);
            }
            return false;
        }

        internal static bool RejectsClassContract(Action action, Type requested)
        {
            try { action(); }
            catch (ArgumentException error)
            {
                return error.Message.Contains(requested.FullName!)
                    && error.Message.Contains("must be an interface");
            }
            return false;
        }

        private static async Task<int> Main(string[] args)
        {
            try
            {
                if (args.FirstOrDefault() == "awaitable-shutdown")
                {
                    await AwaitableShutdownChecks.Run(args[1]);
                    return 0;
                }
                if (args.FirstOrDefault() == "bridge-registration")
                {
                    await BridgeRegistrationChecks.Run(args[1]);
                    return 0;
                }
                if (args.FirstOrDefault() == "execution-guards")
                {
                    await ExecutionGuardChecks.Run();
                    return 0;
                }
                if (args.FirstOrDefault() == "registration-once")
                {
                    await RegistrationOnceChecks.Run(args[1]);
                    return 0;
                }
                if (args.FirstOrDefault() == "root-submission")
                {
                    await RootSubmissionChecks.Run(args[1]);
                    return 0;
                }
                if (args.FirstOrDefault() == "lifecycle")
                {
                    await LifecycleChecks.Run(args[1]);
                    return 0;
                }
                if (args.FirstOrDefault() == "creation-lifetime")
                {
                    await CreationLifetimeChecks.Run(args[1]);
                    return 0;
                }
                if (args.FirstOrDefault() == "construction-cleanup")
                {
                    await ConstructionCleanupChecks.Run(args[1]);
                    return 0;
                }
                if (args.FirstOrDefault() == "initialisation-cleanup")
                {
                    await InitialisationCleanupChecks.Run(args[1]);
                    return 0;
                }
                if (args.FirstOrDefault() == "actor-lifecycle")
                {
                    await ActorLifecycleChecks.Run();
                    return 0;
                }
                if (args.FirstOrDefault() == "initialisation")
                {
                    await InitialisationChecks.Run(args[1]);
                    return 0;
                }

                if (args.FirstOrDefault() == "registration")
                    return RegistrationChecks.Run(args.Skip(1).ToArray());

                if (args.FirstOrDefault() == "completion")
                {
                    await CompletionChecks.Run();
                    return 0;
                }

                if (args.FirstOrDefault() == "command-identities")
                {
                    await CommandIdentityChecks.Run();

                    return 0;
                }

                if (args.FirstOrDefault() == "progress-owner")
                {
                    await ProgressOwnerChecks.Run(args.Length > 1 ? args[1] : "host");

                    return 0;
                }

                if (args.FirstOrDefault() == "bridge")
                {
                    await BridgeChecks.Run();

                    return 0;
                }

                if (args.FirstOrDefault() is "abort-wait" or "abort-wait-retry")
                {
                    await AbortWaitChecks.Run(args[0] == "abort-wait-retry");

                    return 0;
                }

                new ParentProbe().VerifyUnbound();
                RFacade.Create<ParentFacade>(new Shell(), RFacade.Child<ChildFacade>());
                await Finished.Task.WaitAsync(TimeSpan.FromSeconds(10));
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }
    }

    public interface IUniqueModel { }
    public interface IAmbiguousModel { }
    public interface IUniqueMediator { }
    public interface IAmbiguousMediator { }
    public interface ICategory { }
    public interface IMissing { }
    public interface IPrimaryView : IRContract { }
    public sealed class PrimaryView : IPrimaryView { }
    public interface ISecondaryView : IRContract { }
    public interface IReplacementView : IRContract { }

    public abstract class ModelBase : RModel { }
    public sealed class PrimaryModel : ModelBase, IUniqueModel, IAmbiguousModel, ICategory { }
    public sealed class SecondaryModel : RModel, IAmbiguousModel { }
    public class ExactBaseModel : RModel { }
    public sealed class ExactDerivedModel : ExactBaseModel { }
    public sealed class ReplacementModel : RModel, IUniqueModel { }

    public abstract class MediatorBase : RMediator<IPrimaryView>
    {
        internal int ActorAttachedCalls { get; private set; }

        internal int ActorDetachedCalls { get; private set; }

        internal int ActorCalls { get; private set; }

        internal void TouchActor() => WithActor(_ => ActorCalls++);

        protected override void ActorAttached(IPrimaryView actor) => ActorAttachedCalls++;

        protected override void ActorDetached(IPrimaryView actor) => ActorDetachedCalls++;
    }

    public sealed class PrimaryMediator : MediatorBase, IUniqueMediator, IAmbiguousMediator, ICategory { }

    public sealed class SecondaryMediator : RMediator<ISecondaryView>, IAmbiguousMediator
    {
        protected override void ActorAttached(ISecondaryView actor) { }
        protected override void ActorDetached(ISecondaryView actor) { }
    }

    public sealed class ReplacementMediator : RMediator<IReplacementView>, IUniqueMediator
    {
        protected override void ActorAttached(IReplacementView actor) { }
        protected override void ActorDetached(IReplacementView actor) { }
    }

    public sealed class ParentFacade : RFacade
    {
        internal static ParentFacade Instance = null!;
        internal readonly PrimaryModel Primary = new();
        internal readonly PrimaryMediator Presenter = new();
        internal readonly ExactBaseModel Exact = new();

        public ParentFacade() => Instance = this;

        protected override IRModel[] RegisterModels()
            => new IRModel[] { Primary, new SecondaryModel(), Exact, new ExactDerivedModel() };

        protected override RMediator[] RegisterMediators()
            => new RMediator[] { Presenter, new SecondaryMediator() };

        protected override RCommandBase CreateInitialiseCommand(IRInitialisation? initialisation)
            => new ParentInitialise();

        internal void StartChecks() => Execute(new ParentProbe());


    }

    public sealed class ChildFacade : RFacade
    {
        internal static ChildFacade Instance = null!;
        internal readonly PrimaryModel Primary = new();
        internal readonly PrimaryMediator Presenter = new();

        public ChildFacade() => Instance = this;

        protected override IRModel[] RegisterModels() => new IRModel[] { Primary };
        protected override RMediator[] RegisterMediators() => new RMediator[] { Presenter };
        protected override RCommandBase CreateInitialiseCommand(IRInitialisation? initialisation)
            => new ChildProbe();
    }

    internal sealed class ParentInitialise : RCommand<RVoid>
    {
        protected override Task<RVoid> Run()
        {
            InitialiseFacadeChild<ChildFacade>();
            ParentFacade.Instance.StartChecks();
            return Complete();
        }
    }

    internal sealed class ParentProbe : RCommand<RVoid>
    {
        private void VerifyCommandAccessors(ParentFacade facade)
        {
            Program.Check(ReferenceEquals(ModelByContract<IUniqueModel>(), facade.Primary)
                && ReferenceEquals(MediatorByContract<IUniqueMediator>(), facade.Presenter),
                "command accessors return the original registered instances through interfaces");
            Program.Check(Program.RejectsClassContract(() => ModelByContract<ModelBase>(), typeof(ModelBase))
                && Program.RejectsClassContract(() => MediatorByContract<PrimaryMediator>(), typeof(PrimaryMediator)),
                "command contract queries reject class arguments");
            Program.Check(ReferenceEquals(Model<PrimaryModel>(), facade.Primary)
                && ReferenceEquals(ModelOrNull<PrimaryModel>(), facade.Primary)
                && ReferenceEquals(Mediator<PrimaryMediator>(), facade.Presenter)
                && ReferenceEquals(MediatorOrNull<PrimaryMediator>(), facade.Presenter),
                "command required and optional class access return registered instances");
            Program.Check(ReferenceEquals(ModelOrNull<ModelBase>(), facade.Primary)
                && ReferenceEquals(MediatorOrNull<MediatorBase>(), facade.Presenter)
                && ReferenceEquals(Model<ModelBase>(), facade.Primary)
                && ReferenceEquals(Mediator<MediatorBase>(), facade.Presenter),
                "command required and optional class access resolve unique derived registrations");
            Program.Check(Program.ThrowsLookup(() => Model<ReplacementModel>(), facade.GetType().FullName!, typeof(ReplacementModel).FullName!)
                && Program.ThrowsLookup(() => Mediator<ReplacementMediator>(), facade.GetType().FullName!, typeof(ReplacementMediator).FullName!),
                "command missing required class access names owner and requested type");
        }

        public void VerifyUnbound()
        {
            Program.Check(ModelByContract<IUniqueModel>() == null
                && MediatorByContract<IUniqueMediator>() == null,
                "unbound commands return null without consulting another facade");
            Program.Check(Program.RejectsClassContract(() => ModelByContract<ModelBase>(), typeof(ModelBase))
                && Program.RejectsClassContract(() => MediatorByContract<PrimaryMediator>(), typeof(PrimaryMediator)),
                "unbound contract queries also reject class arguments");
            Program.Check(ModelOrNull<PrimaryModel>() == null
                && MediatorOrNull<PrimaryMediator>() == null,
                "unbound optional class access returns null");
            Program.Check(Program.ThrowsLookup(() => Model<PrimaryModel>(), GetType().FullName!, "not bound", typeof(PrimaryModel).FullName!)
                && Program.ThrowsLookup(() => Mediator<PrimaryMediator>(), GetType().FullName!, "not bound", typeof(PrimaryMediator).FullName!),
                "unbound required class access reports command and requested type");
        }

        protected override async Task<RVoid> Run()
        {
            try
            {
                var facade = ParentFacade.Instance;
                VerifyCommandAccessors(facade);
                Program.Check(ReferenceEquals(ModelByContract<IUniqueModel>(), facade.Primary)
                    && ReferenceEquals(MediatorByContract<IUniqueMediator>(), facade.Presenter),
                    "commands resolve unique model and mediator interface matches");
                Program.Check(Program.RejectsClassContract(() => ModelByContract<ModelBase>(), typeof(ModelBase))
                    && Program.RejectsClassContract(() => MediatorByContract<MediatorBase>(), typeof(MediatorBase)),
                    "bound contract queries reject abstract class arguments");
                Program.Check(Program.RejectsClassContract(() => ModelByContract<PrimaryModel>(), typeof(PrimaryModel))
                    && Program.RejectsClassContract(() => MediatorByContract<PrimaryMediator>(), typeof(PrimaryMediator)),
                    "bound contract queries reject concrete class arguments");
                Program.Check(ModelByContract<IMissing>() == null
                    && MediatorByContract<IMissing>() == null,
                    "zero matches return null in either category");
                Program.Check(ModelByContract<IAmbiguousModel>() == null
                    && MediatorByContract<IAmbiguousMediator>() == null,
                    "multiple interface matches return null in their category");
                Program.Check(ReferenceEquals(ModelByContract<ICategory>(), facade.Primary)
                    && ReferenceEquals(MediatorByContract<ICategory>(), facade.Presenter),
                    "a contract used in both categories does not create cross-category ambiguity");
                Program.Check(ModelByContract<IUniqueMediator>() == null
                    && MediatorByContract<IUniqueModel>() == null,
                    "lookups never search the other category");
                Program.Check(ReferenceEquals(Model<PrimaryModel>(), facade.Primary)
                    && ReferenceEquals(Mediator<PrimaryMediator>(), facade.Presenter)
                    && ReferenceEquals(ModelOrNull<ModelBase>(), facade.Primary)
                    && ReferenceEquals(MediatorOrNull<MediatorBase>(), facade.Presenter),
                    "class lookup returns original concrete and unique derived registrations");
                Program.Check(ReferenceEquals(ModelOrNull<PrimaryModel>(), facade.Primary)
                    && ReferenceEquals(MediatorOrNull<PrimaryMediator>(), facade.Presenter),
                    "bound optional class access returns original instances");
                Program.Check(Program.ThrowsLookup(() => Model<ReplacementModel>(), facade.GetType().FullName!, typeof(ReplacementModel).FullName!)
                    && Program.ThrowsLookup(() => Mediator<ReplacementMediator>(), facade.GetType().FullName!, typeof(ReplacementMediator).FullName!),
                    "bound required class access throws for absent registrations");
                Program.Check(ModelOrNull<ExactBaseModel>() == null
                    && Program.ThrowsLookup(() => Model<ExactBaseModel>(), facade.GetType().FullName!,
                        "multiple", typeof(ExactBaseModel).FullName!, typeof(ExactDerivedModel).FullName!),
                    "class lookup rejects ambiguity without preferring the exact registration");
                Program.Check(MediatorOrNull<RMediator>() == null
                    && Program.ThrowsLookup(() => Mediator<RMediator>(), facade.GetType().FullName!,
                        "multiple", typeof(PrimaryMediator).FullName!, typeof(SecondaryMediator).FullName!),
                    "required mediator base lookup reports ambiguity while optional access returns null");
                Program.Check(ReferenceEquals(Model<ModelBase>(), facade.Primary)
                    && ReferenceEquals(Mediator<MediatorBase>(), facade.Presenter),
                    "bound required class access resolves a unique derived Actor");
                VerifyCurrentRegistries(facade);
                VerifyRepeatRegistration(facade);

                Program.Check(!ReferenceEquals(facade.Primary, ChildFacade.Instance.Primary)
                    && ReferenceEquals(ModelByContract<IUniqueModel>(), facade.Primary),
                    "child initialization leaves the parent's lookup scope unchanged");

                RFacade.EnableDebugMode = true;
                Program.Check(ModelByContract<IAmbiguousModel>() == null
                    && MediatorByContract<IAmbiguousMediator>() == null,
                    "debug diagnostics do not change ambiguous lookup results");
                RFacade.EnableDebugMode = false;
                Program.Finished.TrySetResult();
            }
            catch (Exception error)
            {
                Program.Finished.TrySetException(error);
                throw;
            }

            return await Complete();
        }

        private static void VerifyRepeatRegistration(ParentFacade facade)
        {
            var Actor = new PrimaryView();

            facade.Presenter.TouchActor();
            RFacade.RegisterActor<ParentFacade, IPrimaryView>(Actor);

            Program.Check(facade.Presenter.ActorAttachedCalls == 1
                && facade.Presenter.ActorDetachedCalls == 0
                && facade.Presenter.ActorCalls == 1,
                "first Actor registration initialises the mediator and drains pending calls once");

            RFacade.RegisterActor<ParentFacade, IPrimaryView>(Actor);
            RFacade.RegisterActor<ParentFacade, IPrimaryView>(Actor);

            Program.Check(facade.Presenter.ActorAttachedCalls == 1
                && facade.Presenter.ActorDetachedCalls == 0
                && facade.Presenter.ActorCalls == 1,
                "repeat registration of the same Actor neither reinitialises nor detaches the mediator nor replays pending calls");

            RFacade.UnregisterActor<ParentFacade, IPrimaryView>(Actor);

            Program.Check(facade.Presenter.ActorDetachedCalls == 1,
                "one final unregister detaches the repeatedly registered Actor once");
        }

        private void VerifyCurrentRegistries(ParentFacade facade)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            object registry = typeof(RFacade).GetField("actors", flags)!.GetValue(facade)!;
            var models = (Dictionary<Type, IRModel>)registry.GetType()
                .GetField("modelsDictionary", flags)!.GetValue(registry)!;
            var mediators = (Dictionary<Type, RMediator>)registry.GetType()
                .GetField("mediatorsDictionary", flags)!.GetValue(registry)!;
            var replacementModel = new ReplacementModel();
            var replacementMediator = new ReplacementMediator();

            // Test-only mutations demonstrate that no earlier result is cached.
            try
            {
                models.Add(typeof(ReplacementModel), replacementModel);
                mediators.Add(typeof(ReplacementMediator), replacementMediator);
                Program.Check(ModelByContract<IUniqueModel>() == null
                    && MediatorByContract<IUniqueMediator>() == null,
                    "newly ambiguous registries invalidate earlier unique results immediately");
                models.Remove(typeof(PrimaryModel));
                mediators.Remove(typeof(PrimaryMediator));
                Program.Check(ReferenceEquals(ModelByContract<IUniqueModel>(), replacementModel)
                    && ReferenceEquals(MediatorByContract<IUniqueMediator>(), replacementMediator),
                    "each lookup returns the current unique Actor instead of a cached instance");
                models.Remove(typeof(ReplacementModel));
                mediators.Remove(typeof(ReplacementMediator));
                Program.Check(ModelByContract<IUniqueModel>() == null
                    && MediatorByContract<IUniqueMediator>() == null,
                    "removed matches are absent on the next lookup");
            }
            finally
            {
                models.Remove(typeof(ReplacementModel));
                mediators.Remove(typeof(ReplacementMediator));
                models[typeof(PrimaryModel)] = facade.Primary;
                mediators[typeof(PrimaryMediator)] = facade.Presenter;
            }
        }
    }

    internal sealed class ChildProbe : RCommand<RVoid>
    {
        protected override Task<RVoid> Run()
        {
            Program.Check(ReferenceEquals(ModelByContract<IUniqueModel>(), ChildFacade.Instance.Primary)
                && ReferenceEquals(MediatorByContract<IUniqueMediator>(), ChildFacade.Instance.Presenter),
                "child commands search only the child's registries");
            Program.Check(ModelOrNull<ExactBaseModel>() == null
                && MediatorOrNull<SecondaryMediator>() == null,
                "optional class lookup cannot borrow parent-only actors");
            Program.Check(Program.ThrowsLookup(() => Model<ExactBaseModel>(), typeof(ChildFacade).FullName!, typeof(ExactBaseModel).FullName!)
                && Program.ThrowsLookup(() => Mediator<SecondaryMediator>(), typeof(ChildFacade).FullName!, typeof(SecondaryMediator).FullName!),
                "required class lookup rejects parent-only actors in the child scope");
            Program.Check(ReferenceEquals(ModelByContract<IAmbiguousModel>(), ChildFacade.Instance.Primary)
                && ReferenceEquals(MediatorByContract<IAmbiguousMediator>(), ChildFacade.Instance.Presenter),
                "matches in another facade do not make a unique child lookup ambiguous");
            return Complete();
        }
    }

    internal sealed class Shell : IRShell
    {
        public bool ShowMessageBox(string title, string message, bool isYesNo = false)
            => false;
        public void HandleStateChanged(RMVC.RApplicationState state, string? detail = null)
        {
            if (state != RMVC.RApplicationState.Failed) return;
            string message = detail ?? "";
            string facadeName = message.StartsWith("Facade: ") ? message.Split('\n')[0][8..].Trim() : "";
            if (message.StartsWith("Facade: ")) message = message[(message.IndexOf('\n') + 1)..];
            Program.Finished.TrySetException(new Exception(facadeName + ": " + message));
        }
    }
}
