using RMVC;
using RMVCApp.Sample.Core;

namespace RMVCApp.Forms 
{
    public partial class MainForm : Form, IRAppShell 
    {
        private ProgressForm? _progressForm = null;
        public MainForm() 
        {

            RMVCAppFacade.Create(typeof(RMVCAppFacade), this);

            InitializeComponent();
            mainView1.mainFormParent = this;
        }


        public void SetAppEnabled(bool doEnable) 
        {

        }

        public Task<bool> ShowMessageBox(string title, string message, bool isYesNo = false) 
        {
            MessageBoxButtons messageBoxButtons = isYesNo ? MessageBoxButtons.OKCancel : MessageBoxButtons.OK;

            MessageBoxIcon messageBoxIcon = MessageBoxIcon.Information;

            var result = MessageBox.Show(message, title, messageBoxButtons, messageBoxIcon);

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
    }
}
