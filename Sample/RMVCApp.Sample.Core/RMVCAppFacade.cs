using global::RMVC;
using RMVCApp.Sample.Core.Shared;

namespace RMVCApp.Sample.Core 
{
    public class RMVCAppFacade : RFacade 
    {
        internal static RMVCAppFacade? Instance => RFacade.FacadeInstance<RMVCAppFacade>();

        internal IRAppShell? App => AppShell;

        // Models
        internal CounterModel? CounterModel => base.Model<CounterModel>();
        internal WeatherModel? WeatherModel => base.Model<WeatherModel>();
        internal ProgressModel? ProgressModel => base.Model<ProgressModel>(); 

        // View Mediators
        internal MainMediator? MainMediator => base.Mediator<MainMediator>();
        internal NavigationMediator? NavigationMediator => base.Mediator<NavigationMediator>();
        internal HomeMediator? HomeMediator => base.Mediator<HomeMediator>();
        internal CounterMediator? CounterMediator => base.Mediator<CounterMediator>();
        internal WeatherMediator? WeatherMediator => base.Mediator<WeatherMediator>();
        internal ProgressMediator? ProgressMediator => base.Mediator<ProgressMediator>();

        protected override RModel[] RegisterModels() 
        {
            return new RModel[] 
            {
                new CounterModel(),
                new WeatherModel(),
                new ProgressModel()
            };
        }
        protected override RMediator[] RegisterMediators() 
        {
            return new RMediator[]
            {                
                new MainMediator(typeof(IMainView)),
                new NavigationMediator(typeof(INavigationView)),
                new HomeMediator(typeof(IHomeView)),
                new CounterMediator(typeof(ICounterView)),
                new WeatherMediator(typeof(IWeatherView)),
                new ProgressMediator(typeof(IProgressView))
            };
        }
        protected override RCommandBase RegisterStartupCommand() 
        {
            return new StartupCmd();
        }

        protected override RCommandBase? RegisterProgressCompleteCommand() 
        {
            return new SetProgressCompleteCmd();
        }

        protected override RCommandBase? RegisterProgressUpdateCommand(RProgress[] progress) 
        {
            return new SetProgressUpdateCmd(progress);
        }
    }
}