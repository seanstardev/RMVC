using RMVCApp.Sample.Core;
using RMVCApp.Sample.Core.Shared;

namespace RMVCApp.Forms 
{
    public partial class HomeView : UserControl, IHomeView 
    {
        public HomeView() 
        {
            InitializeComponent();
            RMVCAppFacade.RegisterActor<IHomeView>(this);
        }
        public event Action<string>? ShowRmvcMessageEvt;
        public event Action? RunRmvcProgressTestEvt;

        public void SetView() 
        {

        }
        protected void HandleDisposing() 
        {
            RMVCAppFacade.UnregisterActor<IHomeView>(this);
        }
        private void testButton_Click(object sender, EventArgs e) {
            ShowRmvcMessageEvt?.Invoke("Hello from RMVC.");
        }

        private void rmvcProgressBtn_Click(object sender, EventArgs e) {
            RunRmvcProgressTestEvt?.Invoke();
        }
    }
}
