using System.Windows.Threading;
using RestaurantPos.Application;

namespace RestaurantPos.Desktop;

public sealed class LocalBackupScheduler(IBackupService backupService)
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromHours(12) };
    private bool started;
    public void Start()
    {
        if (started) return;
        started = true;
        timer.Tick += (_, _) => _ = Task.Run(TryBackupAsync);
        timer.Start();
        _ = Task.Run(TryBackupAsync);
    }
    public Task CreateNowAsync() => backupService.CreateBackupAsync();
    private async Task TryBackupAsync() { try { await backupService.CreateBackupAsync(); } catch { /* Backups must never stop the POS screen. */ } }
}
