using System.Collections;
using System.Reflection;
using RMVC;

namespace RMVC.ContractLookupChecks
{
    internal static class CommandLockChecks
    {
        private static string scenario = "";
        private static readonly A sharedA = new();
        private static IRCommandLock[] registrations = { sharedA, new B(), new C() };
        private static readonly List<Probe> probes = new();
        private static readonly HostShell shell = new();
        private static int cleanups;
        private static Probe? progressProbe;
        private static bool childLocal;
        private static bool noLocks;
        private static int startups;

        internal static async Task Run(string mode)
        {
            scenario = mode;
            RFacade.EnableDebugMode = mode.EndsWith("-debug") || mode == "duplicate-declaration";

            if (mode.StartsWith("registration-"))
            {
                await CheckRegistration(mode);
                return;
            }

            if (mode == "constructor-lookup")
            {
                bool rejected = false;

                try
                {
                    _ = new ConstructorLookup();
                }
                catch (InvalidOperationException)
                {
                    rejected = true;
                }

                Check(rejected, "constructor lookup is rejected");
                return;
            }

            noLocks = mode is "no-locks" or "load-no-locks";
            childLocal = mode == "facade-isolation";

            if (childLocal)
                RFacade.Create<Host>(shell, RFacade.Child<OtherHost>());
            else
                RFacade.Create<Host>(shell);

            await Wait(shell.Ready.Task);
            await TestRuntime.WaitIdle();

            switch (mode)
            {
                case "admission": await Admission(); break;
                case "depth": await Depth(); break;
                case "cancel-waiter": await CancelWaiter(); break;
                case "cancel-running": await CancelRunning(); break;
                case "cancel-depth": await CancelDepth(); break;
                case "retained-extra": await RetainedExtra(); break;
                case "race": await Races(); break;
                case "overlap": await Children("overlap"); break;
                case "discarded": await Children("discarded"); break;
                case "completed-discard": await Children("completed-discard"); break;
                case "untyped": await Children("untyped"); break;
                case "exit-child": await Children("exit-child"); break;
                case "exit-fault": await ExitFault(); break;
                case "body-fault": await BodyFault(); break;
                case "duplicate-declaration": await Declaration(GateSet.Duplicate, false); break;
                case "null-declaration": await Declaration(GateSet.Null, true); break;
                case "null-entry": await Declaration(GateSet.NullEntry, true); break;
                case "foreign": await Declaration(GateSet.Foreign, true); break;
                case "missing": await Declaration(GateSet.Missing, true); break;
                case "mutable-array": await MutableArray(); break;
                case "mutable-registration": await MutableRegistration(); break;
                case "progress-lock": await ProgressLock(false); break;
                case "progress-descendant": await ProgressLock(true); break;
                case "progress-free": await ProgressFree(); break;
                case "shutdown": await Shutdown(false); break;
                case "shutdown-timeout": await Shutdown(true); break;
                case "facade-isolation": await FacadeIsolation(); break;
                case "no-locks": await NoLocks(); break;
                case "load": await Load(); break;
                case "load-no-locks": await Load(); break;
                case "startup-lock":
                    var afterStartup = New("after startup", GateSet.A);
                    Host.Instance.Submit(afterStartup);
                    await Idle();
                    Check(startups == 1 && afterStartup.Runs == 1, "initialisation lock releases before normal roots run");
                    break;
                default:
                    if (mode.StartsWith("aborted-"))
                        await AbortedChildren(mode);
                    else if (mode == "shutdown-child")
                        await ShutdownChild();
                    else if (mode.StartsWith("fatal-"))
                        await Fatal(mode);
                    else
                        throw new ArgumentException(mode);
                    break;
            }
        }

        private static async Task Admission()
        {
            var releaseB = Signal();
            var releaseA = Signal();
            var holderB = New("B holder", GateSet.B);
            holderB.Body = async _ => { await releaseB.Task; return 41; };
            Host.Instance.Submit(holderB);
            await Wait(holderB.Entered.Task);

            var both = New("A+B waiter", GateSet.AB);
            Host.Instance.Submit(both);
            await Until(() => Waiters() == 1);

            var holderA = New("independent A", GateSet.A);
            holderA.Body = async _ => { await releaseA.Task; return 41; };
            Host.Instance.Submit(holderA);
            await Wait(holderA.Entered.Task);
            Check(both.Runs == 0, "blocked A+B request leaves A available to an independent root");

            releaseB.SetResult();
            await Wait(holderB.Exited.Task);
            Check(both.Runs == 0, "B becoming free does not admit A+B while A is held");
            releaseA.SetResult();
            await Idle();
            Check(both.Runs == 1, "whole-set waiter runs after both locks become free");
        }

        private static async Task Depth()
        {
            var releaseChild = Signal();
            var releaseParent = Signal();
            var leaf = New("leaf reusing A", GateSet.A);
            leaf.Body = async _ => { await releaseChild.Task; return 41; };
            var child = New("child introducing B", GateSet.B);
            var middle = New("ungated middle", GateSet.None);
            middle.Body = async p => await p.Child(leaf);
            child.Body = async p => await p.Child(middle);
            var parent = New("parent retaining A", GateSet.A);
            var childReturned = Signal();
            parent.Body = async p =>
            {
                await p.Child(child);
                childReturned.SetResult();
                await releaseParent.Task;
                return 41;
            };
            Host.Instance.Submit(parent);
            await Wait(leaf.Entered.Task);
            var contenderA = New("outside A", GateSet.A);
            var contenderB = New("outside B", GateSet.B);
            Host.Instance.Submit(contenderA);
            Host.Instance.Submit(contenderB);
            await Until(() => Waiters() == 2);
            releaseChild.SetResult();
            await Wait(childReturned.Task);
            await Wait(contenderB.Entered.Task);
            Check(contenderA.Runs == 0, "child releases introduced B but not ancestor A");
            releaseParent.SetResult();
            await Idle();
            Check(contenderA.Runs == 1 && probes.All(p => p.ErrorsFound.Length == 0),
                "deep reuse and same-type recursive command instances complete cleanly");
        }

        private static async Task CancelWaiter()
        {
            var release = Signal();
            var holder = New("holder", GateSet.A);
            holder.Body = async _ => { await release.Task; return 41; };
            Host.Instance.Submit(holder);
            await Wait(holder.Entered.Task);
            var waiter = NewWaiter(GateSet.AB);
            Host.Instance.Submit(waiter);
            await Until(() => Waiters() == 1);
            await Cancel(typeof(Waiter));
            await Wait(waiter.Exited.Task);
            Check(waiter.Runs == 0 && Waiters() == 0, "cancelled waiter never enters Run and leaves no pending request");
            var next = New("next A", GateSet.A);
            Host.Instance.Submit(next);
            await Until(() => Waiters() == 1);
            Check(next.Runs == 0, "cancelled waiter did not release another root's A");
            release.SetResult();
            await Idle();
            Check(next.Runs == 1, "remaining waiter proceeds after its actual owner releases");
        }

        private static async Task CancelRunning()
        {
            var release = Signal();
            var holder = new Holder { Gates = GateSet.A };
            probes.Add(holder);
            holder.Body = async p => { await release.Task; p.CheckAbort(); return 41; };
            Host.Instance.Submit(holder);
            await Wait(holder.Entered.Task);
            await Cancel(typeof(Holder));
            var next = New("next A", GateSet.A);
            Host.Instance.Submit(next);
            await Until(() => Waiters() == 1);
            Check(next.Runs == 0 && holder.Exits == 0, "cancellation does not unlock a still-running owner");
            release.SetResult();
            await Idle();
            Check(holder.Cancelled && next.Runs == 1, "cooperative owner exit releases its reservation");
        }

        private static async Task Races()
        {
            int simultaneous = 0;
            int peak = 0;

            for (int index = 0; index < 150; index++)
            {
                var release = Signal();
                var holder = New("race holder", GateSet.A);
                holder.Body = async _ =>
                {
                    if (Interlocked.Increment(ref simultaneous) != 1)
                        throw new Exception("Concurrent holder entered");
                    try { await release.Task; return 41; }
                    finally { Interlocked.Decrement(ref simultaneous); }
                };
                Host.Instance.Submit(holder);
                await Wait(holder.Entered.Task);
                var waiter = NewWaiter(GateSet.A);
                waiter.Body = _ =>
                {
                    int count = Interlocked.Increment(ref simultaneous);
                    peak = Math.Max(peak, count);
                    Interlocked.Decrement(ref simultaneous);
                    return Task.FromResult(41);
                };
                Host.Instance.Submit(waiter);
                await Until(() => Waiters() == 1);
                await Task.WhenAll(Task.Run(() => release.TrySetResult()), Cancel(typeof(Waiter)));
                await Idle();
                Check(Waiters() == 0 && simultaneous == 0 && holder.Exits == 1 && waiter.Exits == 1,
                    "grant/cancel race drains exactly once");
            }

            Check(peak <= 1, "150 grant/cancellation races preserve exclusion and leave no leaks");
        }

        private static async Task RetainedExtra()
        {
            var release = Signal();
            var child = New("child A", GateSet.A);
            child.Body = async _ => { await release.Task; return 41; };
            var parent = New("parent A+B", GateSet.AB);
            parent.Body = async p => await p.Child(child);
            Host.Instance.Submit(parent);
            await Wait(child.Entered.Task);
            var contender = New("independent B", GateSet.B);
            Host.Instance.Submit(contender);
            await Until(() => Waiters() == 1);
            Check(contender.Runs == 0, "parent retains its extra B around an A-only child");
            release.SetResult();
            await Idle();
            Check(contender.Runs == 1, "extra reservation releases at parent exit");
        }

        private static async Task CancelDepth()
        {
            var releaseB = Signal();
            var releaseParent = Signal();
            var holderB = New("B holder", GateSet.B);
            holderB.Body = async _ => { await releaseB.Task; return 41; };
            Host.Instance.Submit(holderB);
            await Wait(holderB.Entered.Task);
            var child = NewWaiter(GateSet.B);
            var parent = new Holder { Gates = GateSet.A };
            probes.Add(parent);
            parent.Body = async p =>
            {
                await p.Child(child);
                await releaseParent.Task;
                return 41;
            };
            Host.Instance.Submit(parent);
            await Until(() => Waiters() == 1);
            await Cancel(typeof(Holder));
            var contender = New("A after cancelled child", GateSet.A);
            Host.Instance.Submit(contender);
            await Until(() => Waiters() == 2);
            Check(child.Runs == 0 && child.Exits == 0 && contender.Runs == 0 && parent.Exits == 0,
                "aborted descendant keeps waiting and retains ancestor A until actual cleanup exits");
            releaseB.SetResult();
            await Wait(child.Exited.Task);
            Check(child.Runs == 1 && child.Cancelled && contender.Runs == 0,
                "waiting child enters with inherited abort after its required B becomes free");
            releaseParent.SetResult();
            await Idle();
            Check(contender.Runs == 1 && parent.Cancelled, "ancestor releases only on its own exit");
        }

        private static async Task AbortedChildren(string mode)
        {
            var releaseB = Signal();
            Probe? holderB = null;

            if (mode == "aborted-held-gate")
            {
                holderB = New("B holder", GateSet.B);
                holderB.Body = async _ =>
                {
                    await releaseB.Task;

                    return 41;
                };
                Host.Instance.Submit(holderB);
                await Wait(holderB.Entered.Task);
            }

            var child = New("cleanup child", mode == "aborted-ancestor" ? GateSet.A : GateSet.B);
            bool cleanupRan = false;
            child.Body = p =>
            {
                Check(p.Cancelled && p.HasError, "cleanup enters with inherited abort/error-or-abort still true");
                cleanupRan = true;

                if (mode == "aborted-checkpoint")
                    p.CheckAbort();

                return Task.FromResult(41);
            };

            var parent = new Holder { Gates = GateSet.A };
            probes.Add(parent);
            parent.Body = async p =>
            {
                await Cancel(typeof(Holder));

                if (mode == "aborted-finally")
                {
                    try
                    {
                        p.CheckAbort();

                        return 0;
                    }
                    finally
                    {
                        await p.Child(child);
                    }
                }

                if (mode == "aborted-deep")
                {
                    var middle = New("ungated middle", GateSet.None);
                    var leaf = New("ancestor A leaf", GateSet.A);
                    leaf.Body = q =>
                    {
                        Check(q.Cancelled, "deep descendant retains abort through an ungated intermediate");

                        return Task.FromResult(41);
                    };
                    child.Body = async q =>
                    {
                        cleanupRan = q.Cancelled;

                        return await q.Child(leaf);
                    };
                    middle.Body = async q => await q.Child(child);

                    return await p.Child(middle);
                }

                if (mode == "aborted-untyped")
                {
                    await p.UntypedChild(child);

                    return 41;
                }

                return await p.Child(child);
            };
            Host.Instance.Submit(parent);

            if (holderB != null)
            {
                await Until(() => Waiters() == 1);
                Check(child.Runs == 0 && child.Exits == 0 && parent.Cancelled,
                    "already-aborted child waits for the actual B owner instead of skipping or bypassing its gate");
                releaseB.SetResult();
            }

            await Idle();
            Check(cleanupRan && child.Runs == 1 && child.Exits == 1 && parent.Exits == 1,
                "cleanup and parent exit exactly once");
            Check(parent.Cancelled && parent.ErrorsFound.Length == 0 && Waiters() == 0,
                "cleanup does not reset abort, invent an error or leave a pending lock request");

            var after = New("A+B after cleanup", GateSet.AB);
            Host.Instance.Submit(after);
            await Idle();
            Check(after.Runs == 1, "all cleanup reservations release for later roots");
        }

        private static async Task ShutdownChild()
        {
            var releaseB = Signal();
            var holderB = New("shutdown B holder", GateSet.B);
            holderB.Body = async _ =>
            {
                await releaseB.Task;

                return 41;
            };
            Host.Instance.Submit(holderB);
            await Wait(holderB.Entered.Task);

            var child = New("shutdown cleanup", GateSet.B);
            var parent = New("shutdown parent A", GateSet.A);
            parent.Body = async p => await p.Child(child);
            Host.Instance.Submit(parent);
            await Until(() => Waiters() == 1);
            var stopping = RFacade.Shutdown(60);

            try
            {
                await stopping;

                throw new Exception("Shutdown timeout missing.");
            }
            catch (TimeoutException)
            {
                Check(cleanups == 0 && child.Exits == 0 && parent.Exits == 0,
                    "shutdown timeout retains the waiting child and does not clean actors underneath it");
            }

            releaseB.SetResult();
            await Until(() => cleanups == 1);
            Check(child.Runs == 1 && child.Cancelled && child.Exits == 1 && parent.Exits == 1,
                "shutdown drains aborted child cleanup before model cleanup");
        }

        private static async Task Children(string mode)
        {
            var release = Signal();
            var child = New("child", GateSet.A);
            child.Body = async _ => { await release.Task; return 41; };
            var parent = New("parent", GateSet.A);
            var bodyReturned = Signal();
            bool rejected = false;

            if (mode == "exit-child")
            {
                parent.ExitAction = success =>
                {
                    try { _ = parent.Child(New("invalid exit child", GateSet.A)); }
                    catch (InvalidOperationException) { rejected = true; }
                };
            }
            else
            {
                parent.Body = async p =>
                {
                    if (mode == "completed-discard")
                    {
                        release.SetResult();
                        _ = p.Child(child);
                    }
                    else if (mode == "untyped")
                    {
                        release.SetResult();
                        await p.UntypedChild(child);
                    }
                    else
                    {
                        Task pending = p.Child(child);
                        if (mode == "overlap")
                        {
                            try { await p.Child(New("overlap", GateSet.A)); }
                            catch (InvalidOperationException) { rejected = true; }
                        }
                        bodyReturned.SetResult();
                        if (mode == "overlap") await pending;
                    }
                    return 41;
                };
            }

            Host.Instance.Submit(parent);
            if (mode is "overlap" or "discarded")
            {
                await Wait(bodyReturned.Task);
                var outsider = New("outside A", GateSet.A);
                Host.Instance.Submit(outsider);
                await Until(() => Waiters() == 1);
                Check(parent.Exits == 0 && outsider.Runs == 0, "pending child retains ancestor ownership through parent exit");
                release.SetResult();
            }
            await Idle();
            Check(mode is not ("overlap" or "exit-child") || rejected, "overlapping/exiting child submission rejected where required");
            if (mode == "discarded") Check(parent.ErrorsFound.Any(e => e.Contains("child was still running")), "discarded pending child misuse is recorded");
            if (mode == "completed-discard") Check(parent.ErrorsFound.Length == 0, "already completed discarded task is not falsely claimed detectable");
            if (mode == "untyped") Check(child.Runs == 1 && parent.ErrorsFound.Length == 0, "untyped child shares and returns reservation cleanly");
        }

        private static async Task BodyFault()
        {
            var child = New("throwing child", GateSet.B);
            bool cleaned = false;
            child.Body = _ =>
            {
                try { throw new InvalidOperationException("body fixture"); }
                finally { cleaned = true; }
            };
            var parent = New("error parent", GateSet.A);
            parent.Body = async p => { await p.Child(child); Check(p.HasError, "child error remains shared"); return 41; };
            Host.Instance.Submit(parent);
            await Idle();
            var next = New("after body fault", GateSet.AB);
            Host.Instance.Submit(next);
            await Idle();
            Check(cleaned && next.Runs == 1 && child.Exits == 1, "body-finally and both reservation owners unwind after errors");
        }

        private static async Task ExitFault()
        {
            var bad = New("bad exit", GateSet.A);
            bad.ExitAction = _ => throw new InvalidOperationException("exit fixture");
            Host.Instance.Submit(bad);
            await Idle();
            var next = New("after exit fault", GateSet.A);
            Host.Instance.Submit(next);
            await Idle();
            Check(next.Runs == 1 && bad.ErrorsFound.Any(e => e.Contains("exit fixture")), "exit-hook fault cannot leak the gate");
        }

        private static async Task Declaration(GateSet set, bool invalid)
        {
            var p = New("declaration", set);
            Host.Instance.Submit(p);
            await Idle();
            Check(invalid ? p.Runs == 0 && p.ErrorsFound.Length > 0 : p.Runs == 1 && p.ErrorsFound.Length == 0,
                "invalid declaration rejects before Run; duplicate declaration reserves once");
            var next = New("valid after declaration", GateSet.A);
            Host.Instance.Submit(next);
            await Idle();
            Check(next.Runs == 1, "declaration error leaves valid subsequent work usable");
        }

        private static async Task MutableArray()
        {
            var release = Signal();
            var holder = New("holder", GateSet.B);
            holder.Body = async _ => { await release.Task; return 41; };
            Host.Instance.Submit(holder);
            await Wait(holder.Entered.Task);
            var waiter = New("mutable declaration", GateSet.AB);
            Host.Instance.Submit(waiter);
            await Until(() => Waiters() == 1);
            waiter.Declarations![0] = null!;
            release.SetResult();
            await Idle();
            Check(waiter.Runs == 1 && waiter.ErrorsFound.Length == 0, "mutating returned declarations cannot change queued lock identity");
        }

        private static async Task MutableRegistration()
        {
            registrations[0] = new A();
            var p = New("registration copy", GateSet.A);
            p.Body = x => { Check(ReferenceEquals(x.Declarations![0], sharedA), "registered identity survives caller array mutation"); return Task.FromResult(41); };
            Host.Instance.Submit(p);
            await Idle();
            Check(p.Runs == 1, "copied registration is usable");
        }

        private static async Task ProgressLock(bool descendant)
        {
            var callback = New("invalid progress", descendant ? GateSet.None : GateSet.A);
            progressProbe = callback;
            var nested = New("invalid progress child", GateSet.A);
            if (descendant) callback.Body = async p => await p.Child(nested);
            var root = New("reporter", GateSet.A);
            root.Body = async p => { await p.Report(); return 41; };
            Host.Instance.Submit(root);
            await Idle();
            Check(root.Runs == 1 && root.ErrorsFound.Length == 0, "callback lock rejection remains callback-local");
            Check(descendant ? nested.Runs == 0 && nested.ErrorsFound.Length > 0 : callback.Runs == 0 && callback.ErrorsFound.Length > 0,
                "progress context cannot acquire business gates directly or through descendants");
        }

        private static async Task ProgressFree()
        {
            var callback = New("progress", GateSet.None);
            progressProbe = callback;
            var root = New("reporter", GateSet.A);
            root.Body = async p => { await p.Report(); return 41; };
            Host.Instance.Submit(root);
            await Idle();
            Check(root.ErrorsFound.Length == 0 && callback.Runs == 1, "ungated progress remains runnable under a business reservation");
        }

        private static async Task Shutdown(bool timeout)
        {
            var release = Signal();
            var holder = New("shutdown holder", GateSet.A);
            holder.Body = async _ => { await release.Task; return 41; };
            Host.Instance.Submit(holder);
            await Wait(holder.Entered.Task);
            var waiter = New("shutdown waiter", GateSet.A);
            Host.Instance.Submit(waiter);
            await Until(() => Waiters() == 1);
            var stopping = RFacade.Shutdown(timeout ? 60u : 5000u);
            await Wait(waiter.Exited.Task);
            Check(holder.Exits == 0 && cleanups == 0, "shutdown does not clean actors while an owner still runs");
            if (timeout)
            {
                try { await stopping; throw new Exception("timeout missing"); }
                catch (TimeoutException) { Check(cleanups == 0, "shutdown timeout does not force actor cleanup or gate release"); }
            }
            release.SetResult();
            await Idle();
            if (timeout == false) await Wait(stopping);
            await Until(() => cleanups == 1);
            Check(waiter.Runs == 0 && holder.Exits == 1, "shutdown drains owners and cancels queued commands before cleanup");
        }

        private static async Task FacadeIsolation()
        {
            var release = Signal();
            var first = New("parent facade A", GateSet.A);
            first.Body = async _ => { await release.Task; return 41; };
            Host.Instance.Submit(first);
            await Wait(first.Entered.Task);
            var second = New("child facade A", GateSet.A);
            OtherHost.Instance.Submit(second);
            await Wait(second.Exited.Task);
            Check(second.Runs == 1, "same lock type in separate facades is independent");
            release.SetResult();
            await Idle();
        }

        private static async Task NoLocks()
        {
            var parent = New("no-lock parent", GateSet.None);
            parent.Body = async p => await p.Child(New("no-lock child", GateSet.None));
            Host.Instance.Submit(parent);
            await Idle();
            Check(probes.All(p => p.Runs == 1 && p.ErrorsFound.Length == 0), "facade without registrations keeps normal execution");
        }

        private static async Task Load()
        {
            int active = 0;
            int peak = 0;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 600; i++)
            {
                var p = New("load " + i, noLocks ? GateSet.None : i % 3 == 0 ? GateSet.AB : GateSet.A);
                p.Body = async _ =>
                {
                    int count = Interlocked.Increment(ref active);
                    peak = Math.Max(peak, count);
                    await Task.Yield();
                    Interlocked.Decrement(ref active);
                    return 41;
                };
                Host.Instance.Submit(p);
            }
            await Idle();
            Check((noLocks || peak == 1) && probes.All(p => p.Runs == 1 && p.Exits == 1) && Waiters() == 0,
                "600 roots preserve the selected policy and drain without a lost wakeup");
            Console.WriteLine("MEASURE 600 roots; noLocks=" + noLocks + "; peak=" + peak + "; ms=" + watch.ElapsedMilliseconds);
        }

        private static async Task Fatal(string mode)
        {
            int count = mode.Contains("three") ? 3 : 2;
            var allEntered = Signal();
            int entered = 0;
            GateSet[] held = { GateSet.A, GateSet.B, GateSet.C };
            for (int index = 0; index < count; index++)
            {
                int position = index;
                var parent = New("fatal owner " + position, held[position]);
                parent.Body = async p =>
                {
                    if (Interlocked.Increment(ref entered) == count) allEntered.TrySetResult();
                    await allEntered.Task;

                    if (mode.Contains("aborted"))
                        RFacade.AbortAllCommands();

                    var child = New("fatal waiter " + position, held[(position + 1) % count]);
                    if (mode.Contains("caught"))
                    {
                        try { await p.Child(child); }
                        catch (Exception) { Console.WriteLine("APPLICATION_CAUGHT_CYCLE"); }
                    }
                    else
                    {
                        await p.Child(child);
                    }
                    return 41;
                };
                Host.Instance.Submit(parent);
            }
            await Task.Delay(TimeSpan.FromSeconds(12));
            throw new Exception("FAIL_NO_FATAL");
        }

        private static async Task CheckRegistration(string mode)
        {
            registrations = mode switch
            {
                "registration-null-array" => null!,
                "registration-null-entry" => new IRCommandLock[] { null! },
                "registration-duplicate" => new IRCommandLock[] { new A(), new A() },
                "registration-value" => new IRCommandLock[] { new ValueLock() },
                _ => new IRCommandLock[] { sharedA }
            };
            bool failed = false;
            try
            {
                if (mode == "registration-shared")
                    RFacade.Create<Host>(shell, RFacade.Child<OtherHost>());
                else
                    RFacade.Create<Host>(shell);
            }
            catch (InvalidOperationException)
            {
                failed = true;
            }
            await Wait(shell.Failed.Task);
            Check((failed || shell.FailureDetail.Contains("RegisterCommandLocks")) && startups == 0,
                "invalid lock registration fails setup before initialisation and notifies the shell");
        }

        private static Probe New(string name, GateSet gates)
        {
            var p = new Probe(name) { Gates = gates };
            probes.Add(p);
            return p;
        }

        private static Waiter NewWaiter(GateSet gates)
        {
            var p = new Waiter { Gates = gates };
            probes.Add(p);
            return p;
        }

        private static async Task Cancel(Type type)
        {
            var command = new CancelCommand(type);
            Host.Instance.Submit(command);
            await Wait(command.Done.Task);
        }

        private static TaskCompletionSource Signal()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);

        private static Task Wait(Task task)
            => task.WaitAsync(TimeSpan.FromSeconds(8));

        private static Task Idle()
            => TestRuntime.WaitIdle();

        private static async Task Until(Func<bool> ready)
        {
            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (ready() == false)
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException("Condition not reached: " + scenario);
                await Task.Delay(1);
            }
        }

        private static int Waiters()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            object coordinator = typeof(RFacade).GetField("commandLockCoordinator", flags)!.GetValue(Host.Instance)!;
            object gate = coordinator.GetType().GetField("gate", flags)!.GetValue(coordinator)!;
            lock (gate)
                return ((ICollection)coordinator.GetType().GetField("waiting", flags)!.GetValue(coordinator)!).Count;
        }

        private static void Check(bool value, string message)
            => Program.Check(value, "command-lock " + scenario + ": " + message);

        private enum GateSet { None, A, B, C, AB, Duplicate, Null, NullEntry, Foreign, Missing }
        private sealed class A : IRCommandLock { }
        private sealed class B : IRCommandLock { }
        private sealed class C : IRCommandLock { }
        private sealed class Missing : IRCommandLock { }
        private struct ValueLock : IRCommandLock { }

        private class Probe : RCommand<int>
        {
            internal GateSet Gates;
            internal IRCommandLock[]? Declarations;
            internal Func<Probe, Task<int>> Body = _ => Task.FromResult(41);
            internal Action<bool>? ExitAction;
            internal readonly TaskCompletionSource Entered = Signal();
            internal readonly TaskCompletionSource Exited = Signal();
            internal int Runs;
            internal int Exits;
            internal readonly string Label;
            internal bool HasError => base.ErrorOrAbort;
            internal bool Cancelled => base.AbortRequested;
            internal string[] ErrorsFound => base.Errors.Select(e => e.Message).ToArray();
            internal Probe(string name) { Label = name; }
            internal Task<int> Child(Probe child) => base.Execute(child);
            internal Task UntypedChild(RCommandBase child) => base.Execute(child);
            internal Task Report() => base.SetProgress(10, "fixture progress");
            internal void CheckAbort() => base.ThrowIfAbortRequested();

            protected override IRCommandLock[] GetLocks()
            {
                Declarations = Gates switch
                {
                    GateSet.None => Array.Empty<IRCommandLock>(),
                    GateSet.A => new IRCommandLock[] { base.Lock<A>() },
                    GateSet.B => new IRCommandLock[] { base.Lock<B>() },
                    GateSet.C => new IRCommandLock[] { base.Lock<C>() },
                    GateSet.AB => new IRCommandLock[] { base.Lock<A>(), base.Lock<B>() },
                    GateSet.Duplicate => new IRCommandLock[] { base.Lock<A>(), base.Lock<A>() },
                    GateSet.Null => null!,
                    GateSet.NullEntry => new IRCommandLock[] { null! },
                    GateSet.Foreign => new IRCommandLock[] { new A() },
                    GateSet.Missing => new IRCommandLock[] { base.Lock<Missing>() },
                    _ => throw new ArgumentOutOfRangeException()
                };
                return Declarations;
            }

            protected override async Task<int> Run()
            {
                Interlocked.Increment(ref Runs);
                Entered.TrySetResult();
                return await Body(this);
            }

            protected override void OnCommandExit(bool noErrorOrAbort)
            {
                Interlocked.Increment(ref Exits);
                try { ExitAction?.Invoke(noErrorOrAbort); }
                finally { Exited.TrySetResult(); }
            }
        }

        private sealed class Waiter : Probe { internal Waiter() : base("waiter") { } }
        private sealed class Holder : Probe { internal Holder() : base("holder") { } }
        private sealed class ConstructorLookup : RCommand<int>
        {
            internal ConstructorLookup() { _ = base.Lock<A>(); }
            protected override Task<int> Run() => Complete(41);
        }

        private sealed class CancelCommand : RCommand<RVoid>
        {
            private readonly Type target;
            internal readonly TaskCompletionSource Done = Signal();
            internal CancelCommand(Type target) { this.target = target; }
            protected override Task<RVoid> Run()
            {
                if (target == typeof(Waiter)) base.AbortRootCommands<Waiter>();
                else base.AbortRootCommands<Holder>();
                Done.TrySetResult();
                return Complete();
            }
        }

        public sealed class Host : RFacade
        {
            internal static Host Instance = null!;
            public Host() { Instance = this; }
            internal void Submit(RCommandBase command) => base.Execute(command);
            protected override IRCommandLock[] RegisterCommandLocks() => noLocks ? Array.Empty<IRCommandLock>() : registrations;
            protected override IRModel[] RegisterModels() => new IRModel[] { new CleanupModel() };
            protected override RMediator[] RegisterMediators() => Array.Empty<RMediator>();
            protected override RCommandBase CreateInitialiseCommand(IRInitialisation? initialisation) => new Startup();
            protected override RCommandBase? CreateProgressUpdateCommand(string root, RProgress[] reports)
            {
                var p = progressProbe;
                progressProbe = null;
                return p;
            }
        }

        public sealed class OtherHost : RFacade
        {
            internal static OtherHost Instance = null!;
            public OtherHost() { Instance = this; }
            internal void Submit(RCommandBase command) => base.Execute(command);
            protected override IRCommandLock[] RegisterCommandLocks()
                => new IRCommandLock[] { scenario == "registration-shared" ? sharedA : new A(), new B(), new C() };
            protected override IRModel[] RegisterModels() => Array.Empty<IRModel>();
            protected override RMediator[] RegisterMediators() => Array.Empty<RMediator>();
            protected override RCommandBase CreateInitialiseCommand(IRInitialisation? initialisation) => new OtherStartup();
        }

        private sealed class Startup : RCommand<RVoid>
        {
            protected override IRCommandLock[] GetLocks()
                => scenario == "startup-lock"
                    ? new IRCommandLock[] { base.Lock<A>() }
                    : Array.Empty<IRCommandLock>();

            protected override Task<RVoid> Run()
            {
                Interlocked.Increment(ref startups);
                if (childLocal || scenario == "registration-shared")
                    base.InitialiseFacadeChild<OtherHost>(null);
                return Complete();
            }
        }

        private sealed class OtherStartup : RCommand<RVoid>
        {
            protected override Task<RVoid> Run() => Complete();
        }

        private sealed class CleanupModel : RModel
        {
            public override Task Cleanup()
            {
                Interlocked.Increment(ref cleanups);
                return Task.CompletedTask;
            }
        }

        private sealed class HostShell : IRShell
        {
            internal readonly TaskCompletionSource Ready = Signal();
            internal readonly TaskCompletionSource Failed = Signal();
            internal string FailureDetail = "";
            public void HandleStateChanged(RApplicationState state, string? detail = null)
            {
                if (state == RApplicationState.Running) Ready.TrySetResult();
                if (state == RApplicationState.Failed)
                {
                    FailureDetail = detail ?? "";
                    Failed.TrySetResult();
                }
            }
            public bool ShowMessageBox(string title, string message, bool isYesNo = false) => false;
        }
    }
}
