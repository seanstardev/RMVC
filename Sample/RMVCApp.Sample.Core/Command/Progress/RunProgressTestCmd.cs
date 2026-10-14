using global::RMVC;
using RMVCApp.Sample.Core.Command;
using System.Threading.Tasks;

namespace RMVCApp.Sample.Core 
{
    internal static class RMVCAppConstants 
    {
        public static int GetNextUpdateDelayMilliseconds() 
        {
            // Random random = new Random();
            // return random.Next(100, 1001);
            return 500;
        }
    }

    internal class RunProgressTestCmd : RMVCAppCommandBase<RVoid>
    {
        protected override bool AllowConcurrentRoots => false;

        public RunProgressTestCmd()
        {
            _ = base.SetProgressTitle("Total Progress");
        }

        protected override async Task<RVoid> Run()
        {
            await base.SetProgress("Working...");

            await base.Execute(new RunProgressWorker1Cmd("Worker 1a"), 50d);

            if (base.ErrorOrAbort)
                return await Complete();

            await base.Execute(new RunProgressWorker1Cmd("Worker 1b"), 50d);

            return await Complete();
        }
    }

    internal class RunProgressWorker1Cmd : RMVCAppCommandBase<RVoid>
    {
        public RunProgressWorker1Cmd(string title) 
        {
            _ = base.SetProgressTitle(title);
        }

        private readonly int totalParts = 3;

        protected override async Task<RVoid> Run() 
        {
            for (int i = 0; i < totalParts; i++) 
            {
                await base.Execute(new RunProgressWorker2Cmd("Worker 2"), 100d / totalParts);

                if (base.ErrorOrAbort)
                    return await Complete();

                var part = i + 1;

                await base.SetProgress(part, totalParts, $"Worker 1: {part}/{totalParts}.");
            }

            return await Complete();
        }
    }

    internal class RunProgressWorker2Cmd : RMVCAppCommandBase<RVoid>
    {
        public RunProgressWorker2Cmd(string title) 
        {
            _ = base.SetProgressTitle(title);
        }
        
        private readonly int totalParts = 3;

        protected override async Task<RVoid> Run() 
        {
            for (int i = 0; i < totalParts; i++) 
            {
                await base.Execute(new RunProgressWorker3Cmd("Worker 3"), 100d / totalParts);

                if (base.ErrorOrAbort)
                    return await Complete();

                var part = i + 1;
                await base.SetProgress(part, totalParts, $"Worker 2: {part}/{totalParts}.");
            }

            return await Complete();
        }
    }

    internal class RunProgressWorker3Cmd : RMVCAppCommandBase<RVoid>
    {
        public RunProgressWorker3Cmd(string title) 
        {
            _ = base.SetProgressTitle(title);
        }

        private readonly int totalParts = 3;
        
        protected override async Task<RVoid> Run() 
        {
            for (int i = 0; i < totalParts; i++) 
            {
                await Task.Delay(
                    RMVCAppConstants.GetNextUpdateDelayMilliseconds());

                if (base.ErrorOrAbort)
                    return await Complete();

                var part = i + 1;

                await base.SetProgress(part, totalParts, $"Worker 3: {part}/{totalParts}.");
            }

            return await Complete();
        }
    }
}