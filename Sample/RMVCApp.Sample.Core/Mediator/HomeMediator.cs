using RMVC;
using RMVCApp.Sample.Core.Shared;

namespace RMVCApp.Sample.Core 
{
    internal class HomeMediator : RMediator <IHomeView>
    {
        private void OnShowRmvcMessage(string message) 
        {
            base.Execute(new ShowRmvcMessageCmd(message));
        }

        private void OnRunRmvcProgressTest() 
        {
            base.Execute(new RunProgressTestCmd());
        }

        protected override void Initialised() 
        {
            actor!.ShowRmvcMessageEvt += OnShowRmvcMessage;
            actor!.RunRmvcProgressTestEvt += OnRunRmvcProgressTest;
        }
        protected override void Disposing() 
        {
            actor!.ShowRmvcMessageEvt -= OnShowRmvcMessage;
            actor!.RunRmvcProgressTestEvt -= OnRunRmvcProgressTest;
        }
    }
}
