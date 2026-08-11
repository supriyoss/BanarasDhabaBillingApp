using System.Windows.Threading;
using RestaurantPos.Application;

namespace RestaurantPos.Desktop;

public sealed class LocalBackupScheduler(IBackupService backupService)
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromHours(12) };
    public void Start()
    {
        timer.Tick += async (_, _) => await TryBackupAsync();
        timer.Start();
        _ = TryBackupAsync();
    }
    public Task CreateNowAsync() => backupService.CreateBackupAsync();
    private async Task TryBackupAsync() { try { await backupService.CreateBackupAsync(); } catch { /* Backups must never stop the POS screen. */ } }
}
