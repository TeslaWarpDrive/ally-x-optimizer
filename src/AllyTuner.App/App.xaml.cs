using AllyTuner.Core.Logging;
using Microsoft.UI.Xaml;

namespace AllyTuner;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        Services = AppServices.Create();
        Services.Log.Info($"Ally Tuner {Services.AppVersion} starting.");

        UnhandledException += (_, e) => Services.Log.Error("Unhandled UI exception.", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Services.Log.Error("Unhandled exception.", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
            Services.Log.Error("Unobserved task exception.", e.Exception);

        InitializeComponent();
    }

    /// <summary>App-wide services, created once at startup.</summary>
    public static AppServices Services { get; private set; } = null!;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Closed += (_, _) => Services.Log.Info("Ally Tuner closed.");
        _window.Activate();
    }
}
