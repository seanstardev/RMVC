using System;
using System.Diagnostics;

namespace RMVCApp.Sample.Core.Shared 
{
    public static class HelperUtil 
    {
        public static void Log(object? logMessage) 
        {
            logMessage = "[App.RMVC] " + logMessage;
            Debug.WriteLine($"{logMessage}");
            Console.WriteLine($"{logMessage}");
        }
    }
}
