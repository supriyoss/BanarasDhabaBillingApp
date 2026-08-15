using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using RestaurantPos.Desktop;
using Xunit;

namespace RestaurantPos.Tests;

public sealed class AccessLicenseServiceTests
{
    [Fact]
    public async Task New_installation_receives_thirty_days_of_access()
    {
        using var fixture = new LicenseFixture(new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero));
        var status = await fixture.Service.GetStatusAsync();

        Assert.True(status.IsActive);
        Assert.Equal(30, status.DaysRemaining);
        Assert.Equal(fixture.Clock.GetUtcNow().AddDays(30), status.AccessUntilUtc);
        Assert.False(string.IsNullOrWhiteSpace(status.InstallationId));
    }

    [Fact]
    public async Task Access_expires_without_deleting_the_license_state()
    {
        using var fixture = new LicenseFixture(new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero));
        await fixture.Service.GetStatusAsync();
        fixture.Clock.Advance(TimeSpan.FromDays(31));

        var status = await fixture.Service.GetStatusAsync();

        Assert.False(status.IsActive);
        Assert.True(File.Exists(fixture.StatePath));
    }

    [Fact]
    public async Task Signed_code_for_installation_restores_access()
    {
        using var fixture = new LicenseFixture(new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero));
        var initial = await fixture.Service.GetStatusAsync();
        fixture.Clock.Advance(TimeSpan.FromDays(31));
        var code = fixture.CreateCode(initial.InstallationId, fixture.Clock.GetUtcNow().AddDays(30));

        var result = await fixture.Service.ApplyRenewalCodeAsync(code);

        Assert.True(result.Success);
        Assert.True(result.Status.IsActive);
        Assert.Equal(30, result.Status.DaysRemaining);
    }

    [Fact]
    public async Task Code_for_another_installation_is_rejected()
    {
        using var fixture = new LicenseFixture(new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero));
        await fixture.Service.GetStatusAsync();
        var code = fixture.CreateCode("OTHERINSTALLATION", fixture.Clock.GetUtcNow().AddDays(60));

        var result = await fixture.Service.ApplyRenewalCodeAsync(code);

        Assert.False(result.Success);
        Assert.Contains("different installation", result.Message);
    }

    [Fact]
    public async Task Backwards_clock_is_detected()
    {
        using var fixture = new LicenseFixture(new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero));
        await fixture.Service.GetStatusAsync();
        fixture.Clock.Advance(TimeSpan.FromDays(2));
        await fixture.Service.GetStatusAsync();
        fixture.Clock.Advance(TimeSpan.FromDays(-1));

        var status = await fixture.Service.GetStatusAsync();

        Assert.False(status.IsActive);
        Assert.True(status.ClockRollbackDetected);
    }

    [Fact]
    public async Task Registry_backup_prevents_trial_reset_when_file_is_removed()
    {
        using var fixture = new LicenseFixture(new DateTimeOffset(2026, 8, 16, 0, 0, 0, TimeSpan.Zero), useRegistry: true);
        var original = await fixture.Service.GetStatusAsync();
        fixture.Clock.Advance(TimeSpan.FromDays(31));
        await fixture.Service.GetStatusAsync();
        File.Delete(fixture.StatePath);

        var restored = await fixture.Service.GetStatusAsync();

        Assert.Equal(original.InstallationId, restored.InstallationId);
        Assert.False(restored.IsActive);
    }

    private sealed class LicenseFixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), $"restaurant-pos-license-{Guid.NewGuid():N}");
        private readonly RSA rsa = RSA.Create(2048);
        private readonly string? registrySubKey;
        public MutableTimeProvider Clock { get; }
        public string StatePath => Path.Combine(root, "access.license");
        public AccessLicenseService Service { get; }

        public LicenseFixture(DateTimeOffset now, bool useRegistry = false)
        {
            Directory.CreateDirectory(root);
            Clock = new MutableTimeProvider(now);
            registrySubKey = useRegistry ? $@"Software\BanarasDhabaPOS\Tests\{Guid.NewGuid():N}" : null;
            Service = new AccessLicenseService(StatePath, rsa.ExportSubjectPublicKeyInfoPem(), Clock, registrySubKey);
        }

        public string CreateCode(string installationId, DateTimeOffset validUntilUtc)
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(new
            {
                installationId,
                validUntilUtc = validUntilUtc.ToString("O"),
                issuedUtc = Clock.GetUtcNow().ToString("O"),
                tokenId = Guid.NewGuid().ToString("N")
            });
            var signature = rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return $"BD1.{Encode(payload)}.{Encode(signature)}";
        }

        private static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        public void Dispose()
        {
            rsa.Dispose();
            if (Directory.Exists(root)) Directory.Delete(root, true);
            if (registrySubKey is not null) Registry.CurrentUser.DeleteSubKeyTree(registrySubKey, false);
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset utcNow = now;
        public override DateTimeOffset GetUtcNow() => utcNow;
        public void Advance(TimeSpan duration) => utcNow += duration;
    }
}
