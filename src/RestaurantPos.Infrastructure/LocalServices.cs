using Microsoft.Data.Sqlite;
using RestaurantPos.Application;
using RestaurantPos.Domain;

namespace RestaurantPos.Infrastructure;

public sealed class LocalBackupService(string databasePath, string backupDirectory, int keepCount = 14) : IBackupService
{
    public Task CreateBackupAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(backupDirectory);
        var destination = Path.Combine(backupDirectory, $"restaurant-{DateTime.Now:yyyyMMdd-HHmmss}.db");
        using var source = new SqliteConnection($"Data Source={databasePath}");
        using var target = new SqliteConnection($"Data Source={destination}");
        source.Open(); target.Open(); source.BackupDatabase(target);
        foreach (var oldFile in Directory.EnumerateFiles(backupDirectory, "restaurant-*.db").OrderByDescending(File.GetCreationTimeUtc).Skip(keepCount)) File.Delete(oldFile);
        return Task.CompletedTask;
    }
}
