namespace RMVCApp.Sample.Core 
{
    using global::RMVC;
    using System.Threading.Tasks;

    public class ShowRmvcMessageCmd : RCommandAsync 
    {
        private readonly string message;

        public ShowRmvcMessageCmd(string message) 
        {
            this.message = message;
        }

        protected override bool EnableAutoUpdate => false;

        protected override async Task RunAsync() 
        {
            if (RMVCAppFacade.Instance?.App != null) 
            {
                bool result = await RMVCAppFacade.Instance.App.ShowMessageBox("Attention", message, false);
            }
        }
    }
}
