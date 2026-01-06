namespace RMVC {
    public abstract class RModel : RCommandExecutorBase 
    {
        public RModel() : base() 
        {

        }
        
        abstract protected internal void Initialise();
    }
}
