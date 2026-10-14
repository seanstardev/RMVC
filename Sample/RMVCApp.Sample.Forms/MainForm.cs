using RMVC;
using RMVCApp.Sample.Core;

namespace RMVCApp.Forms 
{
    public partial class MainForm : Form, IRAppShell 
    {
        private ProgressForm? _progressForm = null;
        public MainForm() 
        {
            RMVCAppFacade.Create<RMVCAppFacade>(this);

            InitializeComponent();
            mainView1.mainFormParent = this;
        }

        public Task<bool> ShowMessageBox(
            string title,
            string message,
            bool isYesNo = false)
        {
            if (InvokeRequired)
            {
                return (Task<bool>)Invoke(
                    new Func<Task<bool>>(
                        () => ShowMessageBox(title, message, isYesNo)));
            }

            var buttons = isYesNo
                ? MessageBoxButtons.OKCancel
                : MessageBoxButtons.OK;

            var result = MessageBox.Show(
                this,
                message,
                title,
                buttons,
                MessageBoxIcon.Information);

            return Task.FromResult(result == DialogResult.OK);
        }

        internal void CloseProgressForm() 
        {
            if (_progressForm != null && _progressForm.Visible) 
            {
                _progressForm.Close();
                _progressForm.Dispose();
                _progressForm = null;
            }
        }

        internal void ShowProgressForm() 
        {
            if (_progressForm == null || _progressForm.IsDisposed) {
                _progressForm = new ProgressForm();
                _progressForm.Show(this); // Show it as a modal dialog overlay
            }
        }

        public void HandleOperationsCompleted()
        {

        }

        public void HandleOperationsStarted()
        {

        }
    }
}
