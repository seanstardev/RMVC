using RMVC;
using RMVCApp.Sample.Core.Shared;
using static RMVCApp.Sample.Core.Shared.Enums;

namespace RMVCApp.Sample.Core 
{
    internal class MainMediator : RMediator <IMainView>
    {
        internal void ShowView(ViewEnum viewEnum)
        {
            actor?.ShowView(viewEnum);
        }

        protected override void Initialised() 
        {

        }

        protected override void Disposing() 
        {

        }
    }
}
