using RMVC;
using System;
using System.Diagnostics;

namespace RMVCApp.Sample.Core 
{
    internal class SetProgressUpdateCmd : RCommand 
    {
        private readonly RProgress[] progress;

        public SetProgressUpdateCmd(RProgress[] progress) 
        {
            this.progress = progress;
        }

        protected override void Run() 
        {
            Debug.WriteLine($"{DateTime.Now} >>> Do update progress...");

            if (RMVCAppFacade.Instance != null && RMVCAppFacade.Instance?.ProgressModel != null)
                RMVCAppFacade.Instance.ProgressModel.Progress = progress;
            
            base.ExecuteCommand(new ShowViewCmd(Shared.Enums.ViewEnum.Progress));
            base.ExecuteCommand(new SetProgressViewCmd());
        
            ProgressDebug();
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
