using System.Threading.Tasks;

namespace RMVC 
{
    public interface IRAppShell 
    {
        void SetAppEnabled(bool doEnable);

        Task<bool> ShowMessageBox(string title, string message, bool isYesNo = false);
    }
}
