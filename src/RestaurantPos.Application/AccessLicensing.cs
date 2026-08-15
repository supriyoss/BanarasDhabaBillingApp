namespace RestaurantPos.Application;

public sealed record AccessLicenseStatus(
    bool IsActive,
    bool ClockRollbackDetected,
    string InstallationId,
    DateTimeOffset AccessUntilUtc,
    int DaysRemaining,
    string Message);

public sealed record LicenseActivationResult(bool Success, string Message, AccessLicenseStatus Status);

public interface IAccessLicenseService
{
    Task<AccessLicenseStatus> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<LicenseActivationResult> ApplyRenewalCodeAsync(string renewalCode, CancellationToken cancellationToken = default);
}
