using RMVC;
using RMVCApp.Sample.Core.Shared;

namespace RMVCApp.Sample.Core 
{
    internal class ProgressMediator : RMediator<IProgressView>
    {
        public void SetView(RProgress[] progress) 
        {
            actor?.SetProgress(progress);
        }

        internal void ResetView() 
        {
            actor?.ResetView();
        }

        protected override void Disposing() 
        {
            actor!.AbortProgressEvt -= OnProgressAbort;
        }

        protected override void Initialised() 
        {
            actor!.AbortProgressEvt += OnProgressAbort;
        }

        private void OnProgressAbort() 
        {
            base.Execute(new AbortProgressCmd());
        }
    }
}
