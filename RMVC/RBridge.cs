using System;

namespace RMVC
{
    public abstract class RBridge : RCommandExecutorBase
    {
        internal abstract Type DestinationFacadeType { get; }
    }

    public abstract class RBridge<TFacade> : RBridge
        where TFacade : RFacade
    {
        internal override Type DestinationFacadeType
            => typeof(TFacade);
    }
}
