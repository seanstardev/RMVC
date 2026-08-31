using RMVC;

namespace RMVCApp.Sample.Core 
{
    internal class ProgressModel : IRModel 
    {
        public RProgress[] Progress { get; set; } = new RProgress[] { };
    }
}
