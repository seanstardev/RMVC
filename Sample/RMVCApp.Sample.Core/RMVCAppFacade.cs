using global::RMVC;

namespace RMVCApp.Sample.Core 
{
    public class RMVCAppFacade : RFacade 
    {
        protected override IRModel[] RegisterModels() 
        {
            return new IRModel[] 
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
                new MainMediator(),
                new NavigationMediator(),
                new HomeMediator(),
                new CounterMediator(),
                new WeatherMediator(),
                new ProgressMediator()
            };
        }

        protected override RCommandBase RegisterInitialiseCommand(object? initialisation) 
        {
            return new StartupCmd();
        }

        protected override RCommandBase? RegisterProgressUpdateCommand(
            string commandRootId, 
            RProgress[] progress)
        {
            return new SetProgressUpdateCmd(commandRootId, progress);
        }

        protected override RCommandBase? RegisterProgressCompleteCommand(
            string commandRootId)
        {
            return new SetProgressCompleteCmd(commandRootId);
        }
    }
}