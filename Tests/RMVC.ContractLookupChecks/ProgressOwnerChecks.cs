using RMVC;

namespace RMVC.ContractLookupChecks
{
    internal static class ProgressOwnerChecks
    {
        private static readonly List<(string Owner, string Kind, string CommandRootId)> received = new();
        private static readonly object gate = new();
        private static readonly TaskCompletionSource hostFinished = NewCompletion();
        private static readonly TaskCompletionSource silentFinished = NewCompletion();
        private static readonly TaskCompletionSource standaloneFinished = NewCompletion();
        private static int childFactories;
        private static readonly List<(string Root, RProgress Item)> progressItems = new();

        private static TaskCompletionSource NewCompletion()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal static async Task Run(string mode = "host")
        {
            Program.Check(typeof(RFacade).GetMethod("Configure") == null,
                "progress ownership needs no public Configure method");

            if (mode == "silent")
            {
                RFacade.Create<SilentHost>(new Shell(), RFacade.Child<UnhandledChild>());
                await silentFinished.Task.WaitAsync(TimeSpan.FromSeconds(10));
                lock (gate)
                    Program.Check(received.Count == 0 && childFactories == 0,
                        "a parent without callbacks does not search its reporting child");
                return;
            }
            if (mode == "standalone")
            {
                RFacade.Create<Standalone>(new Shell());
                await standaloneFinished.Task.WaitAsync(TimeSpan.FromSeconds(10));
                lock (gate)
                    Program.Check(received.Any(x => x.Owner == "standalone" && x.Kind == "update")
                        && received.Any(x => x.Owner == "standalone" && x.Kind == "complete")
                        && received.All(x => x.Owner == "standalone"),
                        "a standalone application owns its progress");
                return;
            }
            RFacade.Create<Host>(new Shell(), RFacade.Child<ReportingChild>());
            await hostFinished.Task.WaitAsync(TimeSpan.FromSeconds(10));

            lock (gate)
            {
                Program.Check(received.Any(item => item.Owner == "host" && item.Kind == "update")
                    && received.Any(item => item.Owner == "host" && item.Kind == "complete")
                    && received.All(item => item.Owner == "host"),
                    "child updates and completion execute in the parent's actor registry");
                Program.Check(received.Select(item => item.CommandRootId).Distinct().Count() == 1,
                    "nested command progress and completion retain the reporting root command ID");
            }

            lock (gate)
            {
                Program.Check(progressItems.Any(item => item.Item.Title == nameof(ReportRoot))
                    && progressItems.Any(item => item.Item.Title == nameof(ReportChild)),
                    "progress Title preserves the root and nested command titles");

                Program.Check(progressItems.Any(item => item.Item.CommandId == item.Root)
                    && progressItems.Any(item => item.Item.CommandId != item.Root),
                    "progress identifies root and nested command executions separately");
            }

            Program.Check(typeof(RProgress).GetProperty("Id") == null
                && typeof(RErrorItem).GetProperty("ChainId") == null,
                "public progress and error payloads expose only the agreed identity names");

            Program.Check(childFactories == 0,
                "a child with its own handlers cannot override parent progress ownership");

        }

        public sealed class OwnerModel : RModel
        {
            public string Value { get; }

            public OwnerModel(string value) => Value = value;
        }

        public sealed class Host : RFacade
        {
            protected override IRModel[] RegisterModels() => [new OwnerModel("host")];
            protected override RMediator[] RegisterMediators() => [];
            protected override RCommandBase CreateInitialiseCommand(IRInitialisation? initialisation)
                => new InitialiseChild<ReportingChild>();
            protected override RCommandBase? CreateProgressUpdateCommand(string commandRootId, RProgress[] progress)
            {
                lock (gate)
                    progressItems.AddRange(progress.Select(item => (commandRootId, item)));

                return new Record("update", commandRootId);
            }
            protected override RCommandBase? CreateProgressCompleteCommand(string commandRootId)
                => new Record("complete", commandRootId, hostFinished);
        }

        public sealed class SilentHost : RFacade
        {
            protected override IRModel[] RegisterModels() => [];
            protected override RMediator[] RegisterMediators() => [];
            protected override RCommandBase CreateInitialiseCommand(IRInitialisation? initialisation)
                => new InitialiseChild<UnhandledChild>();
        }

        public abstract class ChildWithHandlers : RFacade
        {
            protected override IRModel[] RegisterModels() => [new OwnerModel("child")];
            protected override RMediator[] RegisterMediators() => [];
            protected override RCommandBase CreateInitialiseCommand(IRInitialisation? initialisation)
                => new ReportRoot();
            protected override RCommandBase? CreateProgressUpdateCommand(string commandRootId, RProgress[] progress)
            {
                Interlocked.Increment(ref childFactories);
                return new Record("update", commandRootId);
            }
            protected override RCommandBase? CreateProgressCompleteCommand(string commandRootId)
            {
                Interlocked.Increment(ref childFactories);
                return new Record("complete", commandRootId);
            }
        }

        public sealed class ReportingChild : ChildWithHandlers { }
        public sealed class UnhandledChild : ChildWithHandlers { }

        public sealed class Standalone : RFacade
        {
            protected override IRModel[] RegisterModels() => [new OwnerModel("standalone")];
            protected override RMediator[] RegisterMediators() => [];
            protected override RCommandBase CreateInitialiseCommand(IRInitialisation? initialisation)
                => new ReportRoot();
            protected override RCommandBase? CreateProgressUpdateCommand(string commandRootId, RProgress[] progress)
                => new Record("update", commandRootId);
            protected override RCommandBase? CreateProgressCompleteCommand(string commandRootId)
                => new Record("complete", commandRootId, standaloneFinished);
        }

        private sealed class InitialiseChild<TChild> : RCommand<RVoid>
            where TChild : RFacade
        {
            protected override Task<RVoid> Run()
            {
                InitialiseFacadeChild<TChild>();
                return Complete();
            }
        }

        private sealed class ReportRoot : RCommand<RVoid>
        {
            protected override async Task<RVoid> Run()
            {
                await Execute(new ReportChild());
                if (FacadeParentByContract<IRFacade>() is SilentHost)
                    silentFinished.TrySetResult();
                return await Complete();
            }
        }

        private sealed class ReportChild : RCommand<RVoid>
        {
            protected override async Task<RVoid> Run()
            {
                await SetProgress(50, "Reading");
                await SetProgress(100, "Read");
                return await Complete();
            }
        }

        private sealed class Record : RCommand<RVoid>
        {
            private readonly string kind;
            private readonly string commandRootId;
            private readonly TaskCompletionSource? finished;

            public Record(string kind, string commandRootId, TaskCompletionSource? finished = null)
            {
                this.kind = kind;
                this.commandRootId = commandRootId;
                this.finished = finished;
            }

            protected override Task<RVoid> Run()
            {
                lock (gate)
                    received.Add((Model<OwnerModel>().Value, kind, commandRootId));

                finished?.TrySetResult();
                return Complete();
            }
        }
    }
}
