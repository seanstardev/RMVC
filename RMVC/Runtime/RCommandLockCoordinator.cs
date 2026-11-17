using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace RMVC
{
    /// <summary>Schedules facade-local reservations without partial acquisition or implicit child requirements.</summary>
    internal sealed class RCommandLockCoordinator
    {
        internal async Task<Lease> Acquire(RTracker tracker, IRCommandLock[] gates)
        {
            var request = new Request(tracker, gates);

            // Child cleanup must acquire its gates even when its inherited work token is cancelled.
            var admissionToken = tracker.TrackerParent == null
                ? tracker.Token
                : CancellationToken.None;

            lock (gate)
            {
                admissionToken.ThrowIfCancellationRequested();

                if (waiting.Any(item => ReferenceEquals(Root(item.Tracker), Root(tracker))))
                    throw new InvalidOperationException(
                        "A command chain cannot have overlapping command-lock requests.");

                if (Blockers(request).Count == 0)
                {
                    Grant(request);
                }
                else
                {
                    waiting.Add(request);

                    var path = new List<RTracker>();

                    if (FindCycle(Root(tracker), Root(tracker), new HashSet<RTracker>(), path))
                    {
                        var error = new RCommandLockCycleException(
                            "Circular command-lock wait rejected: "
                                + string.Join(" -> ", path.Select(DescribeWait))
                                + "; requesting command=" + Describe(tracker)
                                + "; requested gates=" + Names(gates) + ".");

                        waiting.Remove(request);
                        RCommandLockFailure.Record(tracker, error);

                        throw error;
                    }
                }
            }

            bool pending = request.Completion.Task.IsCompleted == false;

            if (pending)
                Report("COMMAND LOCK WAIT: " + Describe(tracker) + "; gates=" + Names(gates));

            using (admissionToken.Register(() => Cancel(request)))
            {
                var lease = await request.Completion.Task.ConfigureAwait(false);

                if (admissionToken.IsCancellationRequested)
                {
                    lease.Dispose();
                    admissionToken.ThrowIfCancellationRequested();
                }

                if (gates.Length != 0 && RFacade.EnableDebugMode)
                    Report("COMMAND LOCK ENTER: " + Describe(tracker) + "; gates=" + Names(gates)
                        + "; introduced=" + Names(lease.Introduced));

                return lease;
            }
        }

        internal static void Report(string message)
        {
            try
            {
                RFacade.Log(message);
            }
            catch
            {
                // Diagnostics must never leak a reservation or change command completion.
            }
        }

        private void Cancel(Request request)
        {
            bool removed;

            lock (gate)
            {
                removed = waiting.Remove(request);

                if (removed)
                {
                    request.Completion.TrySetCanceled();
                    AdmitAvailable();
                }
            }

            if (removed)
                Report("COMMAND LOCK WAIT CANCELLED: " + Describe(request.Tracker));
        }

        private List<RTracker> Blockers(Request request)
        {
            var blockers = new List<RTracker>();

            foreach (var requestedGate in request.Gates)
            {
                if (owners.TryGetValue(requestedGate, out var owner) == false)
                    continue;

                if (IsAncestor(owner, request.Tracker))
                    continue;

                var blockingRoot = Root(owner);

                if (ReferenceEquals(blockingRoot, Root(request.Tracker)))
                    throw new InvalidOperationException(
                        "Parallel sibling command-lock reuse is not supported: " + Describe(request.Tracker) + ".");

                if (blockers.Contains(blockingRoot) == false)
                    blockers.Add(blockingRoot);
            }

            return blockers;
        }

        private bool FindCycle(
            RTracker current,
            RTracker target,
            HashSet<RTracker> visited,
            List<RTracker> path)
        {
            if (visited.Add(current) == false)
                return false;

            path.Add(current);

            foreach (var request in waiting.Where(item => ReferenceEquals(Root(item.Tracker), current)))
            {
                foreach (var blocker in Blockers(request))
                {
                    if (ReferenceEquals(blocker, target))
                    {
                        path.Add(target);

                        return true;
                    }

                    if (FindCycle(blocker, target, visited, path))
                        return true;
                }
            }

            path.RemoveAt(path.Count - 1);

            return false;
        }

        private void Grant(Request request)
        {
            var introduced = new List<IRCommandLock>();

            foreach (var requestedGate in request.Gates)
            {
                if (owners.ContainsKey(requestedGate) == false)
                {
                    owners.Add(requestedGate, request.Tracker);
                    introduced.Add(requestedGate);
                }
            }

            request.Completion.TrySetResult(new Lease(this, request.Tracker, introduced.ToArray()));
        }

        private void AdmitAvailable()
        {
            // Later disjoint requests may proceed; no FIFO or queue-priority guarantee.
            for (int index = 0; index < waiting.Count;)
            {
                var request = waiting[index];

                if (request.Tracker.TrackerParent == null
                    && request.Tracker.Token.IsCancellationRequested)
                {
                    waiting.RemoveAt(index);
                    request.Completion.TrySetCanceled();
                }
                else if (Blockers(request).Count == 0)
                {
                    waiting.RemoveAt(index);
                    Grant(request);
                }
                else
                {
                    index++;
                }
            }
        }

        private void Release(Lease lease)
        {
            lock (gate)
            {
                foreach (var introduced in lease.Introduced)
                {
                    if (owners.TryGetValue(introduced, out var owner) == false
                        || ReferenceEquals(owner, lease.Tracker) == false)
                        throw new InvalidOperationException(
                            "Command-lock ownership mismatch while releasing " + introduced.GetType().FullName + ".");
                }

                foreach (var introduced in lease.Introduced)
                    owners.Remove(introduced);

                AdmitAvailable();
            }

            if (lease.Introduced.Length != 0 && RFacade.EnableDebugMode)
                Report("COMMAND LOCK RELEASE: " + Describe(lease.Tracker) + "; gates=" + Names(lease.Introduced));
        }

        private static bool IsAncestor(RTracker owner, RTracker tracker)
        {
            for (var parent = tracker.TrackerParent; parent != null; parent = parent.TrackerParent)
            {
                if (ReferenceEquals(parent, owner))
                    return true;
            }

            return false;
        }

        private string DescribeWait(RTracker root)
        {
            var request = waiting.FirstOrDefault(item => ReferenceEquals(Root(item.Tracker), root));

            return request == null
                ? Describe(root)
                : Describe(request.Tracker) + " waiting for " + Names(request.Gates);
        }

        private static RTracker Root(RTracker tracker)
        {
            while (tracker.TrackerParent != null)
                tracker = tracker.TrackerParent;

            return tracker;
        }

        private static string Describe(RTracker tracker)
            => tracker.Facade.GetType().FullName + "/" + tracker.Command.GetType().FullName
                + " [command=" + tracker.CommandId + "; root=" + tracker.CommandRootId + "]";

        private static string Names(IEnumerable<IRCommandLock> gates)
            => string.Join(", ", gates.Select(item => item.GetType().FullName));

        private readonly object gate = new object();

        private readonly Dictionary<IRCommandLock, RTracker> owners =
            new Dictionary<IRCommandLock, RTracker>(new GateComparer());

        private readonly List<Request> waiting = new List<Request>();

        internal sealed class Lease : IDisposable
        {
            internal Lease(
                RCommandLockCoordinator coordinator,
                RTracker tracker,
                IRCommandLock[] introduced)
            {
                this.coordinator = coordinator;
                Tracker = tracker;
                Introduced = introduced;
            }

            internal RTracker Tracker { get; }

            internal IRCommandLock[] Introduced { get; }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref released, 1) == 0)
                    coordinator.Release(this);
            }

            private readonly RCommandLockCoordinator coordinator;

            private int released;
        }

        private sealed class Request
        {
            internal Request(RTracker tracker, IRCommandLock[] gates)
            {
                Tracker = tracker;
                Gates = gates;
            }

            internal RTracker Tracker { get; }

            internal IRCommandLock[] Gates { get; }

            internal TaskCompletionSource<Lease> Completion { get; } =
                new TaskCompletionSource<Lease>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private sealed class GateComparer : IEqualityComparer<IRCommandLock>
        {
            public bool Equals(IRCommandLock? left, IRCommandLock? right)
                => ReferenceEquals(left, right);

            public int GetHashCode(IRCommandLock value)
                => RuntimeHelpers.GetHashCode(value);
        }
    }
}
