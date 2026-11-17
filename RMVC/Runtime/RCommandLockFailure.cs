using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace RMVC
{
    /// <summary>Retains a fatal cycle even if application code catches its exception.</summary>
    internal sealed class RCommandLockFailure
    {
        internal static void Record(RTracker tracker, RCommandLockCycleException error)
        {
            var failure = failures.GetValue(Root(tracker), _ => new RCommandLockFailure());

            lock (failure.gate)
            {
                if (failure.error != null)
                    return;

                failure.error = error;
            }

            tracker.SetError(error.Message);
        }

        internal static void ThrowIfRecorded(RTracker tracker)
        {
            if (tracker.Facade.HasCommandLocks == false)
                return;

            if (failures.TryGetValue(Root(tracker), out var failure) == false)
                return;

            RCommandLockCycleException? error;

            lock (failure.gate)
                error = failure.error;

            if (error != null)
                ExceptionDispatchInfo.Capture(error).Throw();
        }

        internal static void DeliverUnhandled(RTracker tracker)
        {
            if (tracker.Facade.HasCommandLocks == false)
                return;

            if (failures.TryGetValue(Root(tracker), out var failure) == false)
                return;

            RCommandLockCycleException? error;

            lock (failure.gate)
            {
                if (failure.delivered)
                    return;

                failure.delivered = true;
                error = failure.error;
            }

            if (error == null)
                return;

            var dispatch = ExceptionDispatchInfo.Capture(error);

            // A raw callback throws outside Task exception capture. Do not replace
            // this with an unobserved Task, optional logging or Environment.FailFast.
            ThreadPool.QueueUserWorkItem(_ => dispatch.Throw());
        }

        private static RTracker Root(RTracker tracker)
        {
            while (tracker.TrackerParent != null)
                tracker = tracker.TrackerParent;

            return tracker;
        }

        private static readonly ConditionalWeakTable<RTracker, RCommandLockFailure> failures =
            new ConditionalWeakTable<RTracker, RCommandLockFailure>();

        private readonly object gate = new object();

        private RCommandLockCycleException? error;

        private bool delivered;
    }
}
