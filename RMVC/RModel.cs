using System.Threading.Tasks;

namespace RMVC
{
    public abstract class RModel : RCommandExecutorBase, IRModel
    {
        public RModel() : base() { }

        /// <summary>Override to release this model's owned resources; no base call is required.</summary>
        public virtual Task Cleanup() => Task.CompletedTask;
    }
}
