using System.Threading.Tasks;

namespace RMVC
{
    /// <summary>A registered model or proxy; cleanup releases resources owned by this instance.</summary>
    public interface IRModel
    {
        /// <summary>Releases owned resources, including any acquired before initialisation failed.</summary>
        Task Cleanup();
    }
}
