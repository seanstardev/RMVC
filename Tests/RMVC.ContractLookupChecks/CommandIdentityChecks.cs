using RMVC;

namespace RMVC.ContractLookupChecks
{
    internal static class CommandIdentityChecks
    {
        private static readonly TaskCompletionSource<IReadOnlyList<RErrorItem>> finished = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        internal static async Task Run()
        {
            RFacade.Create<Host>();
            var errors = await finished.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var root = errors.Single(item => item.Message == "root error");
            var child = errors.Single(item => item.Message == "child error");

            Program.Check(root.CommandId == root.CommandRootId,
                "a root error identifies the same originating and root command execution");
            Program.Check(child.CommandId != root.CommandId
                && child.CommandRootId == root.CommandId,
                "a nested error identifies its own command and the common root command");
            Program.Check(root.CommandName.EndsWith(nameof(Root))
                && child.CommandName.EndsWith(nameof(Child)),
                "error names and IDs describe the command that recorded each error");
        }

        public sealed class Launcher : RModel
        {
            public void Start() => Execute(new Root());
        }

        public sealed class Host : RFacade
        {
            protected override IRModel[] RegisterModels() => [new Launcher()];
            protected override RMediator[] RegisterMediators() => [];
            protected override RCommandBase CreateInitialiseCommand(object? initialisation)
                => new Initialise();
        }

        private sealed class Initialise : RCommand<RVoid>
        {
            protected override Task<RVoid> Run()
            {
                Model<Launcher>()!.Start();
                return Complete();
            }
        }

        private sealed class Root : RCommand<RVoid>
        {
            protected override async Task<RVoid> Run()
            {
                await Execute(new Child());
                SetError("root error");
                return await Complete();
            }

            protected override void OnCommandExit(bool noErrorOrAbort)
                => finished.TrySetResult(Errors);
        }

        private sealed class Child : RCommand<RVoid>
        {
            protected override Task<RVoid> Run()
            {
                SetError("child error");
                return Complete();
            }
        }
    }
}
