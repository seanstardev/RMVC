namespace RMVC
{
    /// <summary>Identifies a facade-registered command coordination gate.</summary>
    /// <remarks>RMVC owns acquisition and release. Implementations contain no synchronization machinery.</remarks>
    public interface IRCommandLock
    {
    }
}
