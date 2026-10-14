using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using MsBox.Avalonia.Models;
using MsBox.Avalonia;
using RMVCApp.Avalonia.ViewModels;
using RMVCApp.Avalonia.Views;
using System.Diagnostics;
using RMVC;
using Avalonia.Threading;
using System.Threading.Tasks;
using RMVCApp.Sample.Core;

namespace RMVCApp.Avalonia 
{
    public partial class App : Application, IRAppShell 
    {

        public override void Initialize() 
        {
            AvaloniaXamlLoader.Load(this);
        }
        
        public override void OnFrameworkInitializationCompleted() 
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) 
            {
                // Line below is needed to remove Avalonia data validation.
                // Without this line you will get duplicate validations from both Avalonia and CT
                BindingPlugins.DataValidators.RemoveAt(0);

                MainWindow mainWindow = new MainWindow {
                    DataContext = new MainWindowViewModel(),
                };

                RMVCAppFacade.Create<RMVCAppFacade>(this);

                desktop.MainWindow = mainWindow;
            }

            base.OnFrameworkInitializationCompleted();
        }

        public async Task<bool> ShowMessageBox(string? title, string message, bool isYesNo) 
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || 
                desktop.MainWindow is null) 
            {
                Debug.WriteLine("Unable to display message box: desktop application lifetime or main window is null.");
                return false;
            }

            // Ensure UI operations are on the UI thread
            return await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var buttonDefinitions = isYesNo
                    ? new[]
                    {
                        new ButtonDefinition { Name = "Yes" },
                        new ButtonDefinition { Name = "No" }
                    }
                    : new[]
                    {
                        new ButtonDefinition { Name = "OK" }
                    };

                var messageBox = MessageBoxManager.GetMessageBoxCustom(new MessageBoxCustomParams 
                {
                    ContentTitle = title ?? "Confirm",
                    ContentMessage = message ?? string.Empty,
                    ButtonDefinitions = buttonDefinitions,
                    Icon = isYesNo ? Icon.Question : Icon.Success,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

                var dialogResult = 
                    await messageBox.ShowWindowDialogAsync(desktop.MainWindow);
                
                return dialogResult == (isYesNo ? "Yes" : "OK");
            });
        }

        public void HandleOperationsCompleted()
        {

        }

        public void HandleOperationsStarted()
        {

        }
    }
}