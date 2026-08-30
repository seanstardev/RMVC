using RMVC;
using RMVCApp.Sample.Core.Shared;
using System;
using static RMVCApp.Sample.Core.Shared.Enums;

namespace RMVCApp.Sample.Core 
{
    internal class MainMediator : RMediator 
    {
        private IMainView? view => (IMainView?)base.viewBase;
        public MainMediator(Type view) : base(view) 
        { 
        }

        internal void ShowView(ViewEnum viewEnum)
        {
            view?.ShowView(viewEnum);
        }

        protected override void Initialsed() 
        {

        }

        protected override void Disposing() 
        {

        }
    }
}
