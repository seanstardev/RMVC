using RMVCApp.Sample.Core;
using System.Windows;

namespace RMVCApp.WPF 
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application, IAppShell
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            RMVCAppFacade.Create<RMVCAppFacade>(this);
            base.OnStartup(e);
        }

        public void HandleInitialisationFailed(
            string facadeName,
            string message)
        {
            _ = ShowMessageBox(
                "Initialisation failed",
                facadeName + "\n\n" + message,
                false);
        }

        public Task<bool> ShowMessageBox(
            string title,
            string message,
            bool isYesNo = false)
        {
            return Dispatcher.InvokeAsync(() =>
            {
                MessageBoxButton buttons =
                    isYesNo ? MessageBoxButton.YesNo : MessageBoxButton.OK;

                MessageBoxResult result = MessageBox.Show(
                    message, title, buttons, MessageBoxImage.Information);

                return result == MessageBoxResult.Yes ||
                       result == MessageBoxResult.OK;
            }).Task;
        }

        public void HandleOperationsCompleted()
        {

        }

        public void HandleOperationsStarted()
        {

        }
    }
}
