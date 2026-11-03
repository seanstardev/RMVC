using RMVC;
using System.Reflection;

namespace RMVC.ContractLookupChecks;

internal static class RegistrationChecks
{
    internal static IRModel[] ModelsA = [];
    internal static IRModel[] ModelsB = [];
    internal static RMediator[] MediatorsA = [];
    internal static RMediator[] MediatorsB = [];
    internal static bool DuplicateBridges;
    internal static bool IncludesChildB;

    private static object? Owner(RCommandExecutorBase Actor)
        => typeof(RCommandExecutorBase).GetProperty("Facade",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Actor);

    internal static int Run(string[] args)
    {
        string scenario = args[0];
        if (scenario is "repeat-create-preserves-owner" or "concurrent-create-preserves-owner" or "release-composition"
            or "release-partial" or "release-bridges" or "reference-identity")
            return VerifyOwnership(scenario);
        var model = new Model();
        var mediator = new Mediator();
        var plain = new PlainModel();
        switch (scenario)
        {
            case "duplicate-model": ModelsA = [new Model(), new Model()]; break;
            case "duplicate-mediator": MediatorsA = [new Mediator(), new Mediator()]; break;
            case "attachment-collision": MediatorsA = [new Mediator(), new OtherMediator()]; break;
            case "null-model": ModelsA = [null!]; break;
            case "null-mediator": MediatorsA = [null!]; break;
            case "null-model-later": ModelsA = [model, null!]; break;
            case "null-mediator-later": MediatorsA = [mediator, null!]; break;
            case "empty": break;
            case "null-model-array": ModelsA = null!; break;
            case "null-mediator-array": MediatorsA = null!; break;
            case "shared-model": ModelsA = [model]; ModelsB = [model]; break;
            case "shared-mediator": MediatorsA = [mediator]; MediatorsB = [mediator]; break;
            case "shared-plain-model": ModelsA = [plain]; ModelsB = [plain]; break;
            case "separate-instances": ModelsA = [new Model()]; ModelsB = [new Model()]; MediatorsA = [new Mediator()]; MediatorsB = [new Mediator()]; break;
            case "ambiguous": ModelsA = [new Model(), new OtherModel()]; MediatorsA = [new Mediator(), new DifferentContractMediator()]; break;
            default: throw new Exception(scenario);
        }
        Exception? failure = null;
        try { CreateWithFailure<A>( RFacade.Child<B>()); }
        catch (Exception e) { failure = e; }
        bool passed;
        if (scenario.StartsWith("duplicate-") || scenario == "attachment-collision")
            passed = failure is InvalidOperationException;
        else if (scenario.StartsWith("null-"))
        {
            string method = scenario.Contains("mediator") ? "RegisterMediators" : "RegisterModels";
            string detail = scenario.EndsWith("array") ? "returned null" :
                "null entry at index " + (scenario.EndsWith("later") ? "1" : "0");
            passed = failure is InvalidOperationException
                && failure.Message.Contains("Facade 'RMVC.ContractLookupChecks.A'")
                && failure.Message.Contains(method + "()")
                && failure.Message.Contains(detail)
                && Owner(model) == null && Owner(mediator) == null;
        }
        else if (scenario.StartsWith("shared-"))
        {
            object shared = scenario == "shared-model" ? model :
                scenario == "shared-mediator" ? mediator : plain;
            passed = failure is InvalidOperationException
                && failure.Message.Contains(shared.GetType().FullName!)
                && failure.Message.Contains(typeof(A).FullName!)
                && failure.Message.Contains(typeof(B).FullName!)
                && Owner(model) == null && Owner(mediator) == null;

            passed &= Program.ThrowsLookup(() => CreateWithFailure<A>(RFacade.Child<B>()), "only once");
        }
        else if (scenario == "ambiguous")
            passed = failure == null && A.Instance.FindModel<IMatching>() == null && A.Instance.FindMediator<IMatching>() == null && A.Instance.GetModel<Model>() != null && A.Instance.GetMediator<Mediator>() != null;
        else if (scenario == "empty")
            passed = failure == null;
        else
            passed = failure == null && !ReferenceEquals(A.Instance.GetModel<Model>(), B.Instance.GetModel<Model>()) && !ReferenceEquals(A.Instance.GetMediator<Mediator>(), B.Instance.GetMediator<Mediator>());
        Console.WriteLine($"{(passed ? "CONFIRMED" : "FAILED")} {scenario}: {failure?.GetType().Name ?? "registration accepted"}; {failure?.Message}");
        return passed ? 0 : 1;
    }

    // Existing validation checks assert diagnostics through the new startup callback.
    // Their models have synchronous no-op cleanup; delayed cleanup has dedicated checks.
    private static void CreateWithFailure<TFacade>(params RFacadeChildRegistration[] children)
        where TFacade : RFacade, new()
    {
        var shell = new RegistrationShell();
        IncludesChildB = children.Length != 0;
        RFacade.Create<TFacade>(shell, children);
        if (!shell.Finished.Wait(TimeSpan.FromSeconds(8)))
            throw new TimeoutException("Registration fixture did not settle startup.");
        if (shell.Message != null) throw new InvalidOperationException(shell.Message);
    }

    private sealed class RegistrationShell : IRShell
    {
        internal readonly ManualResetEventSlim Finished = new();
        internal string? Message;
        public void HandleStateChanged(RMVC.RApplicationState state, string? detail = null)
        {
            if (state == RMVC.RApplicationState.Running) Finished.Set();
            if (state != RMVC.RApplicationState.Failed) return;
            string message = detail ?? "";
            string facadeName = message.StartsWith("Facade: ") ? message.Split('\n')[0][8..].Trim() : "";
            if (message.StartsWith("Facade: ")) message = message[(message.IndexOf('\n') + 1)..];
            Message = message;
            Finished.Set();
        }
        public bool ShowMessageBox(string title, string message, bool isYesNo = false)
            => false;
    }

    private static int VerifyOwnership(string scenario)
    {
        var model = new Model();
        var mediator = new Mediator();
        var plain = new PlainModel();
        bool passed;
        if (scenario == "reference-identity")
        {
            ModelsA = [new EqualModel()];
            ModelsB = [new EqualModel()];
            CreateWithFailure<A>( RFacade.Child<B>());
            passed = !ReferenceEquals(A.Instance.GetModel<EqualModel>(), B.Instance.GetModel<EqualModel>());
        }
        else if (scenario == "repeat-create-preserves-owner")
        {
            ModelsA = [model, plain];
            MediatorsA = [mediator];
            CreateWithFailure<A>();
            passed = true;
            foreach (object instance in new object[] { model, plain, mediator })
            {
                ModelsB = instance is IRModel m ? [m] : [];
                MediatorsB = instance is RMediator med ? [med] : [];
                try { CreateWithFailure<B>(); passed = false; }
                catch (InvalidOperationException e)
                {
                    passed &= e.Message.Contains("only once")
                        && ReferenceEquals(Owner(model), A.Instance)
                        && ReferenceEquals(Owner(mediator), A.Instance);
                }
            }

        }
        else if (scenario == "concurrent-create-preserves-owner")
        {
            ModelsA = [model];
            ModelsB = [model];
            using var barrier = new Barrier(2);

            Exception? Create(Action create)
            {
                try { barrier.SignalAndWait(TimeSpan.FromSeconds(5)); create(); return null; }
                catch (Exception error) { return error; }
            }
            var first = Task.Run(() => Create(() => CreateWithFailure<A>()));
            var second = Task.Run(() => Create(() => CreateWithFailure<B>()));
            Task.WhenAll(first, second).Wait(TimeSpan.FromSeconds(10));
            Exception?[] errors = [first.Result, second.Result];
            passed = errors.Count(e => e == null) == 1
                && errors.Count(e => e is InvalidOperationException
                    && e.Message.Contains("only once")) == 1
                && ReferenceEquals(Owner(model), first.Result == null ? A.Instance : B.Instance);
        }
        else
        {
            ModelsA = [model];
            MediatorsA = [mediator];
            ModelsB = [plain];
            if (scenario == "release-partial")
                ModelsA = [model, new Model()];
            else if (scenario == "release-composition")
                MediatorsB = [new Mediator(), new OtherMediator()];
            else
                DuplicateBridges = true;
            Exception? failure = null;
            try { CreateWithFailure<A>( RFacade.Child<B>()); }
            catch (Exception error) { failure = error; }
            passed = failure is InvalidOperationException
                && Owner(model) == null && Owner(mediator) == null;
            passed &= Program.ThrowsLookup(() => CreateWithFailure<A>(RFacade.Child<B>()), "only once");
        }
        Console.WriteLine($"{(passed ? "PASS" : "FAILED")} ownership {scenario}");
        return passed ? 0 : 1;
    }
}
public interface IMatching { }
public interface IView : IRContract { }
public interface IOtherView : IRContract { }
public sealed class Model : RModel, IMatching { }
public sealed class OtherModel : RModel, IMatching { }
public sealed class PlainModel : IRModel { public System.Threading.Tasks.Task Cleanup() => System.Threading.Tasks.Task.CompletedTask; }
public sealed class Mediator : RMediator<IView>, IMatching { protected override void ActorAttached(IView actor) {} protected override void ActorDetached(IView actor) {} }
public sealed class OtherMediator : RMediator<IView> { protected override void ActorAttached(IView actor) {} protected override void ActorDetached(IView actor) {} }
public sealed class DifferentContractMediator : RMediator<IOtherView>, IMatching { protected override void ActorAttached(IOtherView actor) {} protected override void ActorDetached(IOtherView actor) {} }
public abstract class ProbeFacade : RFacade
{
    protected override RCommandBase CreateInitialiseCommand(IRInitialisation? value) => new Noop();
}
public sealed class A : ProbeFacade
{
    public static A Instance = null!;
    public A() { Instance = this; }
    protected override IRModel[] RegisterModels()
    {
        return RegistrationChecks.ModelsA;
    }
    protected override RBridge[] RegisterBridges()
        => RegistrationChecks.DuplicateBridges ? [new TestBridge(), new TestBridge()] : [];
    protected override RMediator[] RegisterMediators() => RegistrationChecks.MediatorsA;
}
public sealed class B : ProbeFacade
{
    public static B Instance = null!;
    public B() { Instance = this; }
    protected override IRModel[] RegisterModels()
    {
        return RegistrationChecks.ModelsB;
    }
    protected override RMediator[] RegisterMediators() => RegistrationChecks.MediatorsB;
}
public sealed class Noop : RCommand<RVoid>
{
    protected override Task<RVoid> Run()
    {
        if (!HasFacadeParent && RegistrationChecks.IncludesChildB) InitialiseFacadeChild<B>();
        return Complete();
    }
}

public sealed class TestBridge : RBridge<B> { }
public sealed class EqualModel : RModel
{
    public override bool Equals(object? other) => other is EqualModel;
    public override int GetHashCode() => 0;
}
