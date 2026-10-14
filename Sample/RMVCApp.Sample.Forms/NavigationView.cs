using RMVCApp.Sample.Core;
using RMVCApp.Sample.Core.Shared;

namespace RMVCApp.Forms {
    public partial class NavigationView : UserControl, INavigationView {
        public NavigationView() {
            InitializeComponent();
            RMVCAppFacade.RegisterActor<INavigationView>(this);
        }

        public event Action? ShowHomeViewEvt;
        public event Action? ShowCounterViewEvt;
        public event Action? ShowWeatherViewEvt;
        protected void HandleDisposing() {
            RMVCAppFacade.UnregisterActor<INavigationView>(this);
        }
        private void weatherLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e) {
            ShowWeatherViewEvt?.Invoke();
        }

        private void counterLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e) {
            ShowCounterViewEvt?.Invoke();
        }

        private void homeLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e) {
            ShowHomeViewEvt?.Invoke();
        }
    }
}
