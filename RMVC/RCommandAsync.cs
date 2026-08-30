using System.Threading.Tasks;

namespace RMVC
{
    public abstract class RCommandAsync : RCommandAsyncBase
    {
        protected abstract Task RunAsync();

        internal async Task RunInternalAsync(RTracker rTracker)
        {
            await base.RunInternalCoreAsync(
                rTracker,
                RunAsync);
        }

        internal override void ExecuteUntypedInternal(
            RFacade facade,
            RTracker? rTracker = null,
            double percentCap = 100d)
        {
            // Delegate async command execution
            _ = facade.ExecuteCommandAsync(
                this,
                rTracker,
                percentCap);
        }
    }

    public abstract class RCommandAsync<TResult> : RCommandAsyncBase
    {
        protected abstract Task<TResult> RunAsync();

        internal async Task<TResult> RunInternalAsync(RTracker rTracker)
        {
            TResult result = default!;

            await base.RunInternalCoreAsync(
                rTracker,
                async () =>
                {
                    result = await RunAsync();
                });

            return result;
        }

        internal override void ExecuteUntypedInternal(
            RFacade facade,
            RTracker? rTracker = null,
            double percentCap = 100d)
        {
            // Delegate async command execution
            _ = facade.ExecuteCommandAsync(
                this,
                rTracker,
                percentCap);
        }
    }
}