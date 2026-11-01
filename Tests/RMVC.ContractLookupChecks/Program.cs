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

        private static async Task<int> Main(string[] args)
        {
            try
            {
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
                    await ProgressOwnerChecks.Run();

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
        internal int InitialisedCalls { get; private set; }

        internal int DisposingCalls { get; private set; }

        internal int ActorCalls { get; private set; }

        internal void TouchActor() => WithActor(_ => ActorCalls++);

        protected override void Initialised() => InitialisedCalls++;

        protected override void Disposing() => DisposingCalls++;
    }

    public sealed class PrimaryMediator : MediatorBase, IUniqueMediator, IAmbiguousMediator, ICategory { }

    public sealed class SecondaryMediator : RMediator<ISecondaryView>, IAmbiguousMediator
    {
        protected override void Initialised() { }
        protected override void Disposing() { }
    }

    public sealed class ReplacementMediator : RMediator<IReplacementView>, IUniqueMediator
    {
        protected override void Initialised() { }
        protected override void Disposing() { }
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

        protected override RCommandBase CreateInitialiseCommand(object? initialisation)
            => new ParentProbe();

        internal void VerifyFacadeAccessors()
        {
            Program.Check(ReferenceEquals(ModelByContract<IUniqueModel>(), Primary)
                && ReferenceEquals(MediatorByContract<IUniqueMediator>(), Presenter),
                "facade accessors return the original registered instances through interfaces");
        }
    }

    public sealed class ChildFacade : RFacade
    {
        internal static ChildFacade Instance = null!;
        internal readonly PrimaryModel Primary = new();
        internal readonly PrimaryMediator Presenter = new();

        public ChildFacade() => Instance = this;

        protected override IRModel[] RegisterModels() => new IRModel[] { Primary };
        protected override RMediator[] RegisterMediators() => new RMediator[] { Presenter };
        protected override RCommandBase CreateInitialiseCommand(object? initialisation)
            => new ChildProbe();
    }

    internal sealed class ParentProbe : RCommand<RVoid>
    {
        public void VerifyUnbound()
        {
            Program.Check(ModelByContract<IUniqueModel>() == null
                && MediatorByContract<IUniqueMediator>() == null,
                "unbound commands return null without consulting another facade");
        }

        protected override async Task<RVoid> Run()
        {
            try
            {
                var facade = ParentFacade.Instance;
                facade.VerifyFacadeAccessors();
                Program.Check(ReferenceEquals(ModelByContract<IUniqueModel>(), facade.Primary)
                    && ReferenceEquals(MediatorByContract<IUniqueMediator>(), facade.Presenter),
                    "commands resolve unique model and mediator interface matches");
                Program.Check(ReferenceEquals(ModelByContract<ModelBase>(), facade.Primary)
                    && ReferenceEquals(MediatorByContract<MediatorBase>(), facade.Presenter),
                    "base-class lookup returns the same derived registered actor");
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
                    && Model<ModelBase>() == null && Mediator<MediatorBase>() == null,
                    "existing concrete lookup still uses exact registration types");
                Program.Check(ReferenceEquals(Model<ExactBaseModel>(), facade.Exact)
                    && ModelByContract<ExactBaseModel>() == null,
                    "contract lookup has no exact-type preference when a derived match also exists");
                VerifyCurrentRegistries(facade);
                VerifyRepeatRegistration(facade);

                await GetFacadeChild<ChildFacade>()!.Initialise(null);
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
            var actor = new PrimaryView();

            facade.Presenter.TouchActor();
            RFacade.RegisterActor<ParentFacade, IPrimaryView>(actor);

            Program.Check(facade.Presenter.InitialisedCalls == 1
                && facade.Presenter.DisposingCalls == 0
                && facade.Presenter.ActorCalls == 1,
                "first actor registration initialises the mediator and drains pending calls once");

            RFacade.RegisterActor<ParentFacade, IPrimaryView>(actor);
            RFacade.RegisterActor<ParentFacade, IPrimaryView>(actor);

            Program.Check(facade.Presenter.InitialisedCalls == 1
                && facade.Presenter.DisposingCalls == 0
                && facade.Presenter.ActorCalls == 1,
                "repeat registration of the same actor neither reinitialises nor detaches the mediator nor replays pending calls");

            RFacade.UnregisterActor<ParentFacade, IPrimaryView>(actor);

            Program.Check(facade.Presenter.DisposingCalls == 1,
                "one final unregister detaches the repeatedly registered actor once");
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
                    "each lookup returns the current unique actor instead of a cached instance");
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
            Program.Check(ReferenceEquals(ModelByContract<IAmbiguousModel>(), ChildFacade.Instance.Primary)
                && ReferenceEquals(MediatorByContract<IAmbiguousMediator>(), ChildFacade.Instance.Presenter),
                "matches in another facade do not make a unique child lookup ambiguous");
            return Complete();
        }
    }

    internal sealed class Shell : IRShell
    {
        public void HandleOperationsStarted() { }
        public void HandleOperationsCompleted() { }
        public Task<bool> ShowMessageBox(string title, string message, bool isYesNo = false)
            => Task.FromResult(false);
        public void HandleInitialisationFailed(string facadeName, string message)
            => Program.Finished.TrySetException(new Exception(facadeName + ": " + message));
    }
}
