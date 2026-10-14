using System.Threading.Tasks;
using RMVC;
using RMVCApp.Sample.Core.Command;
using System;
using System.Diagnostics;

namespace RMVCApp.Sample.Core 
{
    internal class SetProgressUpdateCmd : RMVCAppCommandBase<RVoid>
    {
        private readonly RProgress[] progress;
        private string chainId;

        public SetProgressUpdateCmd(string chainId, RProgress[] progress)
        {
            this.chainId = chainId;
            this.progress = progress;
        }

        protected override async Task<RVoid> Run() 
        {
            Debug.WriteLine($"{DateTime.Now} >>> Do update progress...");

            if (base.ProgressModel != null)
                base.ProgressModel.Progress = progress;
            
            await base.Execute(new ShowViewCmd(Shared.Enums.ViewEnum.Progress));
            await base.Execute(new SetProgressViewCmd());
        
            ProgressDebug();

            return await Complete();
        }

        private void ProgressDebug() 
        {
            for (int i = 0; i < progress.Length; i++) 
            {
                var p = progress[i];
                Debug.WriteLine($"{p.Heading}: Part {i}:  {p.Percent}");
            }
        }
    }
}
