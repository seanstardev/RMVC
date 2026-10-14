using RMVC;

namespace RMVCApp.Sample.Core.Command
{
    internal abstract class RMVCAppCommandBase<TResult> : RCommand<TResult>
    {
        protected IRAppShell? App => base.AppShell as IRAppShell;

        // Models
        protected CounterModel? CounterModel => base.Model<CounterModel>();
        protected WeatherModel? WeatherModel => base.Model<WeatherModel>();
        protected ProgressModel? ProgressModel => base.Model<ProgressModel>();

        // View Mediators
        protected MainMediator? MainMediator => base.Mediator<MainMediator>();
        protected NavigationMediator? NavigationMediator => base.Mediator<NavigationMediator>();
        protected HomeMediator? HomeMediator => base.Mediator<HomeMediator>();
        protected CounterMediator? CounterMediator => base.Mediator<CounterMediator>();
        protected WeatherMediator? WeatherMediator => base.Mediator<WeatherMediator>();
        protected ProgressMediator? ProgressMediator => base.Mediator<ProgressMediator>();
    }
}
