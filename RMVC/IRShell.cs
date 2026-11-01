using System.Threading.Tasks;

namespace RMVC
{
    public interface IRShell
    {
        // RFacade calls these when active operations transition between zero and nonzero.
        // Implementations marshal UI access as needed.
        void HandleOperationsStarted();
        void HandleOperationsCompleted();

        // The shell owns UI dispatch and its response to initialisation failure.
        void HandleInitialisationFailed(
            string facadeName,
            string message);

        Task<bool> ShowMessageBox(
            string title,
            string message,
            bool isYesNo = false);
    }
}
