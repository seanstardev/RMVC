using RMVC;

namespace RMVCApp.Sample.Core 
{
    internal class ProgressModel : RModel 
    {
        public RProgress[] Progress { get; set; } = new RProgress[] { };
        protected override void Initialise() 
        {

        }
    }
}
