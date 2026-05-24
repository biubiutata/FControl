using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System.Threading;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FControl;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private Window? _window;
    private static Mutex? _appMutex;
    private const string AppMutexName = "FControl-7222A32D-CF3D-4E32-A2B4-FD93E0C8859C";

    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        // Required for single-file publish: tells the Windows App SDK where runtime content is extracted
        Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", AppContext.BaseDirectory);
        InitializeComponent();
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        _appMutex = new Mutex(true, AppMutexName, out bool createdNew);
        if (!createdNew)
        {
            _appMutex.Dispose();
            _appMutex = null;
            Environment.Exit(0);
            return;
        }

        var startHidden = Services.StartupRegistrationService.IsBackgroundStartupLaunch() &&
            Services.AppServices.Configuration.Current.ColdStartEnabled;

        var mainWindow = new MainWindow(startHidden);
        _window = mainWindow;
        if (startHidden)
        {
            mainWindow.StartHiddenInBackgroundMode();
            return;
        }

        _window.Activate();
    }
}
