using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Infrastructure;

namespace RestaurantPos.Desktop;

public sealed class StartupCoordinator(IServiceScopeFactory scopeFactory)
{
    private readonly object gate = new();
    private Task? initializationTask;

    public bool IsReady
    {
        get { lock (gate) return initializationTask?.IsCompletedSuccessfully == true; }
    }

    public Task InitializeAsync(bool retryFailedInitialization = false)
    {
        lock (gate)
        {
            if (retryFailedInitialization && initializationTask?.IsFaulted == true) initializationTask = null;
            return initializationTask ??= Task.Run(InitializeCoreAsync);
        }
    }

    private async Task InitializeCoreAsync()
    {
        using var scope = scopeFactory.CreateScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
    }
}
