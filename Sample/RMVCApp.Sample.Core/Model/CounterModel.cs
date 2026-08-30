using RMVC;

namespace RMVCApp.Sample.Core 
{
    internal class CounterModel : RModel 
    {
        public CounterModel() 
        {
        }

        public int CounterCount { get; set; } = 0;

        protected override void Initialise() 
        {

        }
    }
}
