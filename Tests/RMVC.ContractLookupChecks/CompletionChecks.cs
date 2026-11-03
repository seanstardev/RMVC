using RMVC;

namespace RMVC.ContractLookupChecks
{
    internal static class CompletionChecks
    {
        private enum Role { Root, Child, Update, Complete, CallbackChild }
        private static readonly TestShell shell = new();
        private static Case? current;

        internal static async Task Run()
        {
            RFacade.Create<Host>(shell);
            await shell.WaitForIdle();

            await CheckCase("root success", c => { }, c => c.Root.ExitSuccess == true);
            await CheckCase("typed child result", c => c.Root.Body = async p =>
            {
                int result = await p.Child(c.New(Role.Child));
                if (result != 41) throw new Exception("Typed child result changed.");
                return result;
            }, c => c.Root.ExitSuccess == true && c.Probes.Single(p => p.Role == Role.Child).ExitSuccess == true);
            await CheckCase("three nested levels", c => c.Root.Body = async p =>
            {
                var child = c.New(Role.Child);
                child.Body = async nested => await nested.Child(c.New(Role.Child));
                return await p.Child(child);
            }, c => c.Probes.Count == 3 && c.Probes.All(p => p.ExitSuccess == true));
            await CheckCase("synchronous Run exception", c => c.Root.Body = _ => throw new InvalidOperationException("run failure"),
                c => c.Root.ExitSuccess == false && c.Root.ErrorMessages.Any());
            await CheckCase("asynchronous Run exception", c => c.Root.Body = async _ =>
            {
                await Task.Yield();
                throw new InvalidOperationException("async run failure");
            }, c => c.Root.ExitSuccess == false && c.Root.ErrorMessages.Any());
            await CheckCase("recorded error", c => c.Root.Body = p =>
            {
                p.Error();
                return Task.FromResult(41);
            }, c => c.Root.ExitSuccess == false && c.Root.ErrorMessages.Any());
            await CheckCase("child exception propagates error state", c => c.Root.Body = async p =>
            {
                var child = c.New(Role.Child);
                child.Body = _ => throw new InvalidOperationException("child failure");
                return await p.Child(child);
            }, c => c.Probes.All(p => p.ExitSuccess == false) && c.Root.ErrorMessages.Any());
            await CheckCase("unrequested cancellation exception is an error", c => c.Root.Body = _ => throw new OperationCanceledException("not requested"),
                c => c.Root.ExitSuccess == false && c.Root.ErrorMessages.Any());
            await CheckCase("cancellation during Run", c => c.Root.Body = p =>
            {
                p.Cancel();
                p.CheckAbort();
                return Task.FromResult(41);
            }, c => c.Root.ExitSuccess == false && c.Root.Aborted && !c.Root.ErrorMessages.Any());
            await CheckCase("cancellation before Run", c => c.Root.CancelBeforeRun = true,
                c => c.Root.RunCount == 0 && c.Root.ExitSuccess == false && c.Root.Aborted);
            await CheckCase("cancellation before nested Run", c => c.Root.Body = async p =>
            {
                p.Cancel();
                return await p.Child(c.New(Role.Child));
            }, c => c.Probes.Single(p => p.Role == Role.Child).RunCount == 0 && c.Probes.All(p => p.ExitSuccess == false));
            await CheckCase("cancellation in nested Run", c => c.Root.Body = async p =>
            {
                var child = c.New(Role.Child);
                child.Body = x => { x.Cancel(); x.CheckAbort(); return Task.FromResult(41); };
                return await p.Child(child);
            }, c => c.Probes.All(p => p.ExitSuccess == false));
            await CheckCase("title setup failure before Run", c => c.Root.ThrowTitle = true,
                c => c.Root.RunCount == 0 && c.Root.ExitSuccess == false && c.Root.ErrorMessages.Any());
            await CheckCase("root exit hook throws", c => c.Root.ThrowExit = true,
                c => c.Root.ExitSuccess == true && c.Root.ErrorMessages.Any());
            await CheckCase("nested exit hook throws", c => c.Root.Body = async p =>
            {
                var child = c.New(Role.Child); child.ThrowExit = true;
                return await p.Child(child);
            }, c => c.Root.ExitSuccess == false && c.Root.ErrorMessages.Any());
            await CheckCase("update and completion callbacks", c => c.Root.Report = true,
                c => c.CompletionFactories == 1 && c.UpdateFactories > 0 && c.Probes.All(p => p.ExitSuccess == true));
            await CheckCase("completion after reported Run error", c =>
            {
                c.Root.Report = true;
                c.Root.Body = _ => throw new InvalidOperationException("reported failure");
            }, c => c.CompletionFactories == 1 && c.Root.ExitSuccess == false);
            await CheckCase("completion after reported cancellation", c =>
            {
                c.Root.Report = true;
                c.Root.Body = p => { p.Cancel(); p.CheckAbort(); return Task.FromResult(41); };
            }, c => c.CompletionFactories == 1 && c.Root.ExitSuccess == false
                && c.Probes.Where(p => p.Role == Role.Complete).All(p => !p.Aborted && p.RunCount == 1));
            await CheckCase("completion despite root exit failure", c => { c.Root.Report = true; c.Root.ThrowExit = true; },
                c => c.CompletionFactories == 1 && c.Root.ErrorMessages.Any());
            await CheckCase("update callback Run failure stays callback-local", c => { c.Root.Report = true; c.ThrowCallbackRun = Role.Update; },
                c => c.Root.ExitSuccess == true && !c.Root.ErrorMessages.Any() && c.CompletionFactories == 1);
            await CheckCase("completion callback Run failure stays callback-local", c => { c.Root.Report = true; c.ThrowCallbackRun = Role.Complete; },
                c => c.Root.ExitSuccess == true && !c.Root.ErrorMessages.Any() && c.CompletionFactories == 1);
            await CheckCase("update callback exit failure", c => { c.Root.Report = true; c.ThrowCallbackExit = Role.Update; },
                c => c.Root.ExitSuccess == false && c.Root.ErrorMessages.Any() && c.CompletionFactories == 1);
            await CheckCase("completion callback exit failure", c => { c.Root.Report = true; c.ThrowCallbackExit = Role.Complete; },
                c => c.Root.ExitSuccess == true && c.Root.ErrorMessages.Any() && c.CompletionFactories == 1);
            await CheckCase("callbacks cannot recursively report", c => { c.Root.Report = true; c.CallbackReports = true; },
                c => c.CompletionFactories == 1 && c.UpdateFactories is > 0 and < 5 && c.Root.ExitSuccess == true);
            await CheckCase("callbacks execute nested commands", c => { c.Root.Report = true; c.CallbackChildren = true; },
                c => c.CompletionFactories == 1 && c.Probes.Any(p => p.Role == Role.CallbackChild) && c.Probes.All(p => p.ExitSuccess == true));
            await CheckCase("update factory failure", c => { c.Root.Report = true; c.ThrowUpdateFactory = true; },
                c => c.Root.ExitSuccess == false && c.CompletionFactories == 1 && c.Root.ErrorMessages.Any());
            await CheckCase("completion factory failure", c => { c.Root.Report = true; c.ThrowCompleteFactory = true; },
                c => c.Root.ExitSuccess == true && c.CompletionFactories == 1 && c.Root.ErrorMessages.Any());
            await CheckCase("no progress means no completion callback", c => { },
                c => c.UpdateFactories == 0 && c.CompletionFactories == 0);

            var reused = new Case(); current = reused; Host.Instance.Submit(reused.Root);
            await reused.Root.Exited.Task.WaitAsync(TimeSpan.FromSeconds(5)); await shell.WaitForIdle();
            bool rejected = false;
            try { Host.Instance.Submit(reused.Root); } catch (InvalidOperationException) { rejected = true; }
            Program.Check(rejected && reused.Root.ExitCount == 1, "completion: single-use rejection does not rerun completion");

            await CheckRejection();
            await CheckConcurrentRoots();
            for (int i = 0; i < 100; i++)
            {
                var stress = new Case(); stress.Root.Report = i % 2 == 0;
                stress.Root.Body = async p => await p.Child(stress.New(Role.Child));
                await Execute(stress);
                if (stress.Probes.Any(p => p.ExitCount != 1) || stress.CompletionFactories != (stress.Root.Report ? 1 : 0))
                    throw new Exception("Completion stress iteration " + i + " failed.");
            }
            Program.Check(true, "completion: 100 repeated nested executions preserve exactly-once exits and callback counts");
        }

        private static async Task CheckCase(string name, Action<Case> configure, Func<Case, bool> verify)
        {
            var c = new Case(); configure(c); await Execute(c);
            Program.Check(c.Probes.All(p => p.ExitCount == 1) && verify(c), "completion: " + name);
        }

        private static async Task Execute(Case c)
        {
            current = c; Host.Instance.Submit(c.Root);
            await c.Root.Exited.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await shell.WaitForIdle();
            if (c.Probes.Any(p => p.ExitCount != 1)) throw new Exception("A dispatched command did not exit exactly once.");
        }

        private static async Task CheckRejection()
        {
            var c = new Case(); current = c;
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            c.Root.AllowOverlap = false;
            c.Root.Body = async _ => { entered.TrySetResult(); await release.Task; return 41; };
            Host.Instance.Submit(c.Root); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var rejected = new Case(); rejected.Root.AllowOverlap = false;
            try
            {
                Host.Instance.Submit(rejected.Root);
                Program.Check(rejected.Root.RunCount == 0 && rejected.Root.ExitCount == 0,
                    "completion: an unadmitted root receives neither Run nor OnCommandExit");
            }
            finally { release.TrySetResult(); }
            await c.Root.Exited.Task.WaitAsync(TimeSpan.FromSeconds(5)); await shell.WaitForIdle();
            Program.Check(c.Root.ExitCount == 1, "completion: admitted root still exits once after overlapping rejection");
        }

        private static async Task CheckConcurrentRoots()
        {
            var cases = Enumerable.Range(0, 50).Select(_ => new Case()).ToArray();
            foreach (var c in cases)
            {
                c.Root.Body = async p => { await Task.Yield(); return await p.Child(c.New(Role.Child)); };
                Host.Instance.Submit(c.Root);
            }
            await Task.WhenAll(cases.Select(c => c.Root.Exited.Task)).WaitAsync(TimeSpan.FromSeconds(10));
            await shell.WaitForIdle();
            Program.Check(cases.All(c => c.Probes.Count == 2 && c.Probes.All(p => p.ExitCount == 1)),
                "completion: 50 concurrent roots and their children each exit once");
        }

        private sealed class Case
        {
            internal readonly List<Probe> Probes = new();
            internal readonly Probe Root;
            internal int UpdateFactories;
            internal int CompletionFactories;
            internal Role? ThrowCallbackRun;
            internal Role? ThrowCallbackExit;
            internal bool CallbackReports;
            internal bool CallbackChildren;
            internal bool ThrowUpdateFactory;
            internal bool ThrowCompleteFactory;
            internal Case() => Root = New(Role.Root);
            internal Probe New(Role role)
            {
                var p = new Probe(this, role); Probes.Add(p); return p;
            }
            internal Probe Callback(Role role)
            {
                if (role == Role.Update) { UpdateFactories++; if (ThrowUpdateFactory) throw new Exception("update factory"); }
                else { CompletionFactories++; if (ThrowCompleteFactory) throw new Exception("complete factory"); }
                var p = New(role); p.ThrowExit = ThrowCallbackExit == role; p.Report = CallbackReports;
                p.Body = async parent =>
                {
                    if (ThrowCallbackRun == role) throw new Exception("callback Run");
                    return CallbackChildren ? await parent.Child(New(Role.CallbackChild)) : 41;
                };
                return p;
            }
        }

        private sealed class Probe : RCommand<int>
        {
            internal readonly Role Role;
            internal readonly TaskCompletionSource Exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal Func<Probe, Task<int>> Body = _ => Task.FromResult(41);
            internal int RunCount;
            internal int ExitCount;
            internal bool? ExitSuccess;
            internal bool ThrowExit;
            internal bool CancelBeforeRun;
            internal bool ThrowTitle;
            internal bool Report;
            internal bool AllowOverlap = true;
            internal bool Aborted => AbortRequested;
            internal string[] ErrorMessages => Errors.Select(e => e.Message).ToArray();
            internal Probe(Case c, Role role) => Role = role;
            protected override bool AllowConcurrentRoots => AllowOverlap;
            internal Task<int> Child(Probe child) => Execute(child);
            internal void Error() => SetError("recorded error");
            internal void Cancel() => AbortAllCommands();
            internal void CheckAbort() => ThrowIfAbortRequested();
            protected override string GetProgressTitle()
            {
                if (CancelBeforeRun) AbortAllCommands();
                if (ThrowTitle) throw new Exception("title failure");
                return Role.ToString();
            }
            protected override async Task<int> Run()
            {
                Interlocked.Increment(ref RunCount);
                if (Report) await SetProgress(10, "running");
                return await Body(this);
            }
            protected override void OnCommandExit(bool noErrorOrAbort)
            {
                Interlocked.Increment(ref ExitCount); ExitSuccess = noErrorOrAbort;
                try { if (ThrowExit) throw new Exception("exit hook failure"); }
                finally { Exited.TrySetResult(); }
            }
        }

        public sealed class Host : RFacade
        {
            internal static Host Instance = null!;
            public Host() => Instance = this;
            internal void Submit(RCommandBase command) => Execute(command);
            protected override IRModel[] RegisterModels() => [];
            protected override RMediator[] RegisterMediators() => [];
            protected override RCommandBase CreateInitialiseCommand(IRInitialisation? initialisation) => new Initialise();
            protected override RCommandBase? CreateProgressUpdateCommand(string commandRootId, RProgress[] progress)
                => current!.Callback(Role.Update);
            protected override RCommandBase? CreateProgressCompleteCommand(string commandRootId)
                => current!.Callback(Role.Complete);
        }
        private sealed class Initialise : RCommand<RVoid>
        {
            protected override Task<RVoid> Run() => Complete();
        }
        private sealed class TestShell : IRShell
        {
            internal Task WaitForIdle() => TestRuntime.WaitIdle();
            public void HandleStateChanged(RMVC.RApplicationState state, string? detail = null)
        {
            if (state != RMVC.RApplicationState.Failed) return;
            string message = detail ?? "";
            string facadeName = message.StartsWith("Facade: ") ? message.Split('\n')[0][8..].Trim() : "";
            if (message.StartsWith("Facade: ")) message = message[(message.IndexOf('\n') + 1)..];
            throw new Exception("Unexpected initialisation failure: " + message);
        }
            public bool ShowMessageBox(string title, string message, bool isYesNo = false) => false;
        }
    }
}
