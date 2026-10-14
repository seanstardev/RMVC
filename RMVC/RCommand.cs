using System.Threading.Tasks;

namespace RMVC
{
    public abstract class RCommand<TResult> : RCommandBase
    {
        protected abstract Task<TResult> Run();

        internal async Task<TResult> RunInternal(RTracker tracker)
        {
            TResult result = default!;

            await RunInternalCore(
                tracker, 
                async () => result = await Run().ConfigureAwait(false)).ConfigureAwait(false);

            return result;
        }

        internal override async Task ExecuteUntypedInternal(RTracker tracker)
            => await RunInternal(tracker).ConfigureAwait(false);
    }
}
