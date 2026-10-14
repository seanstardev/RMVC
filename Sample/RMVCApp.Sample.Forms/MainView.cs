using RMVCApp.Sample.Core;
using RMVCApp.Sample.Core.Shared;

namespace RMVCApp.Forms {
    public partial class MainView : UserControl, IMainView {

        public MainForm? mainFormParent;
        public MainView() {
            InitializeComponent();
            RMVCAppFacade.RegisterActor<IMainView>(this);
        }

        public void ShowView(Enums.ViewEnum viewEnum) {
            if (this.InvokeRequired) {
                this.Invoke(new Action(() => ShowView(viewEnum)));
                return;
            }

            switch (viewEnum) {
                case Enums.ViewEnum.Home:
                    tabControl1.SelectedTab = homePage;
                    break;
                case Enums.ViewEnum.Counter:
                    tabControl1.SelectedTab = counterPage;
                    break;
                case Enums.ViewEnum.Weather:
                    tabControl1.SelectedTab = weatherPage;
                    break;
                case Enums.ViewEnum.Progress:
                    mainFormParent?.ShowProgressForm();
                    break;
                case Enums.ViewEnum.None:
                    mainFormParent?.CloseProgressForm();
                    break;
            }
        }
        protected void HandleDisposing() {
            RMVCAppFacade.UnregisterActor<IMainView>(this);
        }
    }
}
