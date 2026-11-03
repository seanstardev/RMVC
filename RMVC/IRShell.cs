namespace RMVC
{
    /// <summary>The required host boundary for interaction and lifecycle observation, including non-interactive hosts.</summary>
    public interface IRShell
    {
        /// <summary>Reports ordered lifecycle transitions. Failed does not certify resource release.</summary>
        void HandleStateChanged(RApplicationState state, string? message = null);

        /// <summary>
        /// Returns the host decision synchronously; this narrow interaction may keep a
        /// command alive while a user responds. Async-only hosts should throw clearly
        /// here and supply an application-owned async contract selected by application
        /// policy. Do not block an async-only event loop to imitate this default.
        /// </summary>
        bool ShowMessageBox(string title, string message, bool isYesNo = false);
    }
}
