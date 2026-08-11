using System.Windows;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Application;
using RestaurantPos.Infrastructure;

namespace RestaurantPos.Desktop;

public partial class App : System.Windows.Application
{
    private ServiceProvider? services;

    public void Logout()
    {
        if (services is null) return;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        MainWindow?.Close();
        services.GetRequiredService<UserSession>().SignOut();
        if (services.GetRequiredService<LoginWindow>().ShowDialog() != true) { Shutdown(); return; }
        MainWindow = services.GetRequiredService<MainWindow>();
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        MainWindow.Show();
    }
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RestaurantPos");
        Directory.CreateDirectory(root);
        var dbPath = Path.Combine(root, "restaurant.db");
        services = new ServiceCollection().AddDbContext<RestaurantDbContext>(o => o.UseSqlite($"Data Source={dbPath}"))
            .AddSingleton<IOrderCalculator, OrderCalculator>().AddSingleton<IReceiptPrinter, WpfReceiptPrinter>().AddSingleton<PinHasher>().AddSingleton<UserSession>()
            .AddSingleton<IBackupService>(_ => new LocalBackupService(dbPath, Path.Combine(root, "Backups"))).AddSingleton<LocalBackupScheduler>().AddScoped<DatabaseInitializer>().AddScoped<IOrderWorkflow, OrderWorkflow>().AddScoped<IAuthenticationService, AuthenticationService>().AddScoped<IReportingService, ReportingService>().AddScoped<IAdministrationService, AdministrationService>().AddTransient<LoginWindow>().AddTransient<MainWindow>().BuildServiceProvider();
        using (var scope = services.CreateScope()) await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
        services.GetRequiredService<LocalBackupScheduler>().Start();
        if (services.GetRequiredService<LoginWindow>().ShowDialog() != true) { Shutdown(); return; }
        MainWindow = services.GetRequiredService<MainWindow>();
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        MainWindow.Show();
    }
    protected override void OnExit(ExitEventArgs e) { services?.Dispose(); base.OnExit(e); }
}
