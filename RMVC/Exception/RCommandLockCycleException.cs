using System;

namespace RMVC
{
    /// <summary>A programming error that closes a circular command-lock wait.</summary>
    public sealed class RCommandLockCycleException : InvalidOperationException
    {
        internal RCommandLockCycleException(string message)
            : base(message)
        {
        }
    }
}
