using RMVC;

namespace RMVCApp.Sample.Core 
{
    internal class CounterModel : IRModel 
    {
        public CounterModel() 
        {
        }

        public int CounterCount { get; set; } = 0;
    }
}
