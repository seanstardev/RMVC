using System;

namespace RMVC
{
    public sealed class RFacadeChildRegistration
    {
        internal Type FacadeType { get; }

        internal Func<RFacade> Factory { get; }

        internal RFacadeChildRegistration(
            Type facadeType,
            Func<RFacade> factory)
        {
            FacadeType = facadeType;
            Factory = factory;
        }
    }
}
