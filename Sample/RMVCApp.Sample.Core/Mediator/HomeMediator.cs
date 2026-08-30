using RMVC;
using System;
using RMVCApp.Sample.Core.Shared;

namespace RMVCApp.Sample.Core 
{
    internal class HomeMediator : RMediator 
    {
        private IHomeView? view => (IHomeView?)base.viewBase;
        public HomeMediator(Type view) : base(view) 
        { 
        }

        private void OnShowRmvcMessage(string message) 
        {
            base.ExecuteCommand(new ShowRmvcMessageCmd(message));
        }
        private void OnRunRmvcProgressTest() 
        {
            base.ExecuteCommand(new RunProgressTestCmd());
        }
        protected override void Initialsed() 
        {
            view!.ShowRmvcMessageEvt += OnShowRmvcMessage;
            view!.RunRmvcProgressTestEvt += OnRunRmvcProgressTest;
        }
        protected override void Disposing() 
        {
            view!.ShowRmvcMessageEvt -= OnShowRmvcMessage;
            view!.RunRmvcProgressTestEvt -= OnRunRmvcProgressTest;
        }
    }
}
