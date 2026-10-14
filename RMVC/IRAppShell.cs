using System.Threading.Tasks;

namespace RMVC
{
    public interface IRAppShell
    {
        // RFacade calls these when active operations transition between zero and nonzero.
        // Implementations marshal UI access as needed.
        void HandleOperationsStarted();
        void HandleOperationsCompleted();

        Task<bool> ShowMessageBox(
            string title,
            string message,
            bool isYesNo = false);
    }
}
