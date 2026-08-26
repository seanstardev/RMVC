using RMVCApp.Sample.Core;
using RMVCApp.Sample.Core.Shared;

namespace RMVCApp.Forms {
    public partial class CounterView : UserControl, ICounterView {
        public event Action<int>? SetCounterEvt;

        public CounterView() {
            InitializeComponent();
            RMVCAppFacade.RegisterActor(this);
        }


        public void SetCounter(int count) {

            if (this.InvokeRequired) {
                this.Invoke(new Action(() => SetCounter(count)));
                return;
            }

            currentCountLabel.Text = count.ToString();
        }
        protected void HandleDisposing() {
            RMVCAppFacade.UnregisterActor(this);
        }
        private void testButton_Click(object sender, EventArgs e) {
            int count = int.Parse(currentCountLabel.Text);
            count++;
            currentCountLabel.Text = count.ToString();

            SetCounterEvt?.Invoke(count);
        }
    }
}
