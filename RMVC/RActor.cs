namespace RMVC {
    public abstract class RActor 
    {
        public string Name { get { return GetType().Name; } }
        internal RActor() 
        {
            
        }    
    }
}
