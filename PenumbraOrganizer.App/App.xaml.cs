namespace PenumbraOrganizer.App;

using System.Windows.Threading;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PenumbraOrganizer.App.ViewModels;
using PenumbraOrganizer.Infrastructure;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    public App()
    {
        StartupBootstrapLogger.Initialize(Environment.GetCommandLineArgs());
        StartupBootstrapLogger.Stage("process started");
        RegisterCrashHandlers();
        StartupBootstrapLogger.Stage("crash handlers registered");
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            base.OnStartup(e);
            StartupBootstrapLogger.Stage("application resources initialized");

            if (e.Args.Contains("--simulate-startup-crash", StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("Simulated startup failure requested from the command line.");

            var services = new ServiceCollection();
            StartupBootstrapLogger.Stage("service collection created");

            services.AddLogging(builder => builder.AddDebug().SetMinimumLevel(LogLevel.Information));
            services.AddPenumbraOrganizerInfrastructure();
            services.AddSingleton<BackupsViewModel>();
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<MainWindow>();
            StartupBootstrapLogger.Stage("infrastructure services registered");

            _serviceProvider = services.BuildServiceProvider();
            StartupBootstrapLogger.Stage("service provider built");

            _serviceProvider.GetRequiredService<MainViewModel>();
            StartupBootstrapLogger.Stage("MainViewModel resolved");

            var window = _serviceProvider.GetRequiredService<MainWindow>();
            StartupBootstrapLogger.Stage("MainWindow constructed");
            window.Show();
            StartupBootstrapLogger.Stage("MainWindow shown");

            Dispatcher.BeginInvoke(() => StartupBootstrapLogger.Stage("startup completed"));
        }
        catch (Exception ex)
        {
            StartupBootstrapLogger.HandleFatal("startup failed", ex);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }

    private void RegisterCrashHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
                StartupBootstrapLogger.HandleFatal("AppDomain.CurrentDomain.UnhandledException", exception);
            else
                StartupBootstrapLogger.Note("AppDomain.CurrentDomain.UnhandledException received a non-Exception payload.");
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            StartupBootstrapLogger.HandleFatal("TaskScheduler.UnobservedTaskException", args.Exception);
            args.SetObserved();
        };

        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        StartupBootstrapLogger.HandleFatal("DispatcherUnhandledException", e.Exception);
        e.Handled = true;
        Shutdown(-1);
    }
}
