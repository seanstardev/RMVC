using RMVC;
using RMVCApp.Sample.Core.Shared;
using System;

namespace RMVCApp.Sample.Core 
{
    internal class ProgressMediator : RMediator 
    {
        private IProgressView? view => (IProgressView?)base.viewBase;
        public ProgressMediator(Type view) : base(view) 
        {

        }
        public void SetView(RProgress[] progress) 
        {
            view?.SetProgress(progress);
        }
        internal void ResetView() 
        {
            view?.ResetView();
        }

        protected override void Disposing() 
        {
            view!.AbortProgressEvt -= OnProgressAbort;
        }

        protected override void Initialsed() 
        {
            view!.AbortProgressEvt += OnProgressAbort;
            base.ExecuteCommand(new SetProgressViewCmd());
        }

        private void OnProgressAbort() 
        {
            base.ExecuteCommand(new AbortProgressCmd());
        }
    }
}
