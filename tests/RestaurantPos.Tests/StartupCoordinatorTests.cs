using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Desktop;
using RestaurantPos.Infrastructure;
using Xunit;

namespace RestaurantPos.Tests;

public sealed class StartupCoordinatorTests
{
    [Fact]
    public async Task Initialization_RunsOnceInBackgroundAndSignalsReady()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var services = new ServiceCollection()
            .AddDbContext<RestaurantDbContext>(options => options.UseSqlite(connection))
            .AddSingleton<PinHasher>()
            .AddScoped<DatabaseInitializer>()
            .BuildServiceProvider();
        var coordinator = new StartupCoordinator(services.GetRequiredService<IServiceScopeFactory>());

        var first = coordinator.InitializeAsync();
        var second = coordinator.InitializeAsync();

        Assert.Same(first, second);
        await first;
        Assert.True(coordinator.IsReady);
    }
}
