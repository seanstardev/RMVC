using RMVC;
using RMVCApp.Sample.Core;
using RMVCApp.Sample.Core.Shared;


namespace RMVCApp.Forms {
    public partial class ProgressView : UserControl, IProgressView {
        public ProgressView() {
            InitializeComponent();
            RMVCAppFacade.RegisterActor<IProgressView>(this);
        }

        public event Action? AbortProgressEvt;

        public void SetProgress(RProgress[] progress) {
            if (this.InvokeRequired) {
                this.Invoke(new Action(() => SetProgress(progress)));
                return;
            }

            AdjustProgressUIControls(progress.Length);

            for (int i = 0; i < progress.Length; i++) {
                var progressUI = (ProgressUI)progressContainer.Controls[i];
                progressUI.Margin = new Padding(i * 20, 0, 0, 0); // Indent each by 20px more than the last
                progressUI.SetUI(progress[i], i == 0);
            }
        }
        public void ResetView() {

        }


        private void AdjustProgressUIControls(int requiredCount) {

            while (progressContainer.Controls.Count < requiredCount) {
                var progressUI = new ProgressUI();
                progressUI.Margin = new Padding(10, 0, 10, 0);
                progressContainer.Controls.Add(progressUI);
            }

            while (progressContainer.Controls.Count > requiredCount) {
                progressContainer.Controls.RemoveAt(progressContainer.Controls.Count - 1);
            }
        }

        protected void HandleDisposing() {
            RMVCAppFacade.UnregisterActor<IProgressView>(this);
        }

        private void abortBtn_Click(object sender, EventArgs e) {
            AbortProgressEvt?.Invoke();
        }

    }
}
