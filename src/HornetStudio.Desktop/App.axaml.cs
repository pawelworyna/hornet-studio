using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HornetStudio.Core.Database.Abstractions;
using HornetStudio.Core.Database.Common;
using HornetStudio.Core.Logging;
using HornetStudio.Core.Services;
using HornetStudio.Core.Storage;
using HornetStudio.Core.Storage.Security;
using HornetStudio.Desktop.ViewModels;
using HornetStudio.Desktop.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HornetStudio.Desktop;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        var logger = Services.GetRequiredService<ILogger<App>>();
        logger.LogInformation("=================================================");
        logger.LogInformation("HornetStudio {Version} (.NET 11) started.", HornetStudio.Core.Common.AppVersion.DisplayVersion);
        logger.LogInformation("OS: {OS}, Runtime: {Runtime}", Environment.OSVersion, Environment.Version);
        logger.LogInformation("=================================================");

        // Global crash handlers according to best practices
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            logger.LogCritical(ex, "FATAL CRASH: Unhandled domain exception. IsTerminating={IsTerminating}", args.IsTerminating);
        };

        TaskScheduler.UnobservedTaskException += (sender, args) =>
        {
            logger.LogError(args.Exception, "Unobserved task exception captured.");
            args.SetObserved();
        };

        // Ensure database is initialized
        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HornetDbContext>();
            db.Database.EnsureCreated();
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Exit += (s, e) =>
            {
                logger.LogInformation("HornetStudio shutting down gracefully.");
            };

            var mainVm = Services.GetRequiredService<MainViewModel>();
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainVm
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HornetStudio");
        Directory.CreateDirectory(appData);

        var dbPath = Path.Combine(appData, "hornet_studio.db");

        // 1. Production-grade file logging according to best practices
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddHornetFileLogger(options =>
            {
                options.LogDirectory = Path.Combine(appData, "logs");
                options.RetainedDays = 14;
                options.MaxFileSizeMb = 10;
                options.MinLogLevel = LogLevel.Information;
            });
        });

        services.AddDbContext<HornetDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));

        services.AddSingleton<MasterPasswordVault>();
        services.AddSingleton<IDatabaseClientFactory, DatabaseClientFactory>();
        services.AddSingleton<IExportImportService, ExportImportService>();
        services.AddSingleton<IBackupRestoreService, BackupRestoreService>();
        services.AddSingleton<IClipboardService, HornetStudio.Desktop.Services.AvaloniaClipboardService>();
        services.AddSingleton<ISqlFormatterService, SqlFormatterService>();

        services.AddSingleton<MainViewModel>();
    }
}