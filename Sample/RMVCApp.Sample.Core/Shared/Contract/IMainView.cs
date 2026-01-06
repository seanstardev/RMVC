using RMVC;
using static RMVCApp.Sample.Core.Shared.Enums;

namespace RMVCApp.Sample.Core.Shared {
    public interface IMainView : IRContract {
        void ShowView(ViewEnum viewEnum);
    }
}
