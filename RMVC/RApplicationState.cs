namespace RMVC
{
    /// <summary>Framework lifecycle facts, independent of command activity or host exit/restart intent.</summary>
    public enum RApplicationState
    {
        /// <summary>Accepted creation is constructing or initialising the composition.</summary>
        Initialising,
        /// <summary>All initialisers succeeded; ordinary dispatch is available, not necessarily idle.</summary>
        Running,
        /// <summary>Admission is closed and cancellation, draining or cleanup is underway.</summary>
        ShuttingDown,
        /// <summary>Framework shutdown drained work and completed model cleanup inside its budget.</summary>
        Stopped,
        /// <summary>Startup or shutdown failed. Resource release is not certified.</summary>
        Failed
    }
}
