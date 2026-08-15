using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using RestaurantPos.Application;

namespace RestaurantPos.Desktop;

public sealed class AccessLicenseService(string statePath, string publicKeyPem, TimeProvider timeProvider, string? registrySubKey = null) : IAccessLicenseService
{
    private static readonly byte[] StateEntropy = Encoding.UTF8.GetBytes("Banaras Dhaba POS access state v1");
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<AccessLicenseStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var now = timeProvider.GetUtcNow();
            var state = LoadOrCreateState(now);
            var status = CreateStatus(state, now);
            if (!status.ClockRollbackDetected && now > state.LastObservedUtc)
            {
                state.LastObservedUtc = now;
                SaveState(state);
            }
            return status;
        }
        finally { gate.Release(); }
    }

    public async Task<LicenseActivationResult> ApplyRenewalCodeAsync(string renewalCode, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var now = timeProvider.GetUtcNow();
            var state = LoadOrCreateState(now);
            var current = CreateStatus(state, now);
            if (!TryReadRenewalCode(renewalCode, out var payload)) return new(false, "The renewal code is not valid.", current);
            if (!string.Equals(payload.InstallationId, state.InstallationId, StringComparison.OrdinalIgnoreCase)) return new(false, "This renewal code belongs to a different installation.", current);
            if (payload.IssuedUtc > now.AddDays(1)) return new(false, "The renewal code issue date is not valid.", current);
            if (payload.ValidUntilUtc <= now) return new(false, "This renewal code has already expired.", current);
            if (payload.ValidUntilUtc <= current.AccessUntilUtc) return new(false, "This renewal code does not extend the current access period.", current);

            state.RenewalCode = renewalCode.Trim();
            state.LastObservedUtc = now;
            SaveState(state);
            var updated = CreateStatus(state, now);
            return new(true, $"Access restored through {updated.AccessUntilUtc.ToLocalTime():dd MMM yyyy}.", updated);
        }
        finally { gate.Release(); }
    }

    private LicenseState LoadOrCreateState(DateTimeOffset now)
    {
        var candidates = new List<LicenseState>();
        var storedStateFound = false;
        if (File.Exists(statePath))
        {
            storedStateFound = true;
            TryAddState(File.ReadAllBytes(statePath), candidates);
        }
        var registryState = ReadRegistryState();
        if (registryState is not null) { storedStateFound = true; TryAddState(registryState, candidates); }

        if (candidates.Count == 0)
        {
            if (storedStateFound) throw new InvalidDataException("The local access state could not be verified. Contact the application provider.");
            var created = new LicenseState { InstallationId = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)), TrialStartedUtc = now, TrialEndsUtc = now.AddDays(30), LastObservedUtc = now };
            SaveState(created);
            return created;
        }

        var selected = candidates.OrderByDescending(x => x.LastObservedUtc).First();
        SaveState(selected);
        return selected;
    }

    private void SaveState(LicenseState state)
    {
        var directory = Path.GetDirectoryName(statePath) ?? throw new InvalidOperationException("The access-state location is invalid.");
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions);
        var protectedBytes = ProtectedData.Protect(json, StateEntropy, DataProtectionScope.CurrentUser);
        var temporaryPath = statePath + ".tmp";
        File.WriteAllBytes(temporaryPath, protectedBytes);
        File.Move(temporaryPath, statePath, true);
        WriteRegistryState(protectedBytes);
    }

    private static void TryAddState(byte[] protectedBytes, ICollection<LicenseState> candidates)
    {
        try
        {
            var json = ProtectedData.Unprotect(protectedBytes, StateEntropy, DataProtectionScope.CurrentUser);
            var state = JsonSerializer.Deserialize<LicenseState>(json, JsonOptions);
            if (state is not null && !string.IsNullOrWhiteSpace(state.InstallationId) && state.TrialEndsUtc != default) candidates.Add(state);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException) { }
    }

    private byte[]? ReadRegistryState()
    {
        if (string.IsNullOrWhiteSpace(registrySubKey)) return null;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(registrySubKey);
            var encoded = key?.GetValue("AccessState") as string;
            return string.IsNullOrWhiteSpace(encoded) ? null : Convert.FromBase64String(encoded);
        }
        catch (Exception ex) when (ex is FormatException or UnauthorizedAccessException or System.Security.SecurityException) { return null; }
    }

    private void WriteRegistryState(byte[] protectedBytes)
    {
        if (string.IsNullOrWhiteSpace(registrySubKey)) return;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(registrySubKey);
            key?.SetValue("AccessState", Convert.ToBase64String(protectedBytes), RegistryValueKind.String);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException) { }
    }

    private AccessLicenseStatus CreateStatus(LicenseState state, DateTimeOffset now)
    {
        var rollback = now < state.LastObservedUtc.AddMinutes(-5);
        var accessUntil = state.TrialEndsUtc;
        if (!string.IsNullOrWhiteSpace(state.RenewalCode) && TryReadRenewalCode(state.RenewalCode, out var payload) &&
            string.Equals(payload.InstallationId, state.InstallationId, StringComparison.OrdinalIgnoreCase) && payload.ValidUntilUtc > accessUntil)
            accessUntil = payload.ValidUntilUtc;

        var active = !rollback && now <= accessUntil;
        var daysRemaining = active ? Math.Max(0, (int)Math.Ceiling((accessUntil - now).TotalDays)) : 0;
        var message = rollback
            ? "The computer clock appears to have moved backwards. Contact the application provider to restore access."
            : active
                ? $"Access is active through {accessUntil.ToLocalTime():dd MMM yyyy}. {daysRemaining} day{(daysRemaining == 1 ? string.Empty : "s")} remaining."
                : $"Access expired on {accessUntil.ToLocalTime():dd MMM yyyy}. Enter a renewal code to continue.";
        return new(active, rollback, state.InstallationId, accessUntil, daysRemaining, message);
    }

    private bool TryReadRenewalCode(string? code, out RenewalPayload payload)
    {
        payload = new RenewalPayload();
        if (string.IsNullOrWhiteSpace(code)) return false;
        var parts = code.Trim().Split('.');
        if (parts.Length != 3 || parts[0] != "BD1") return false;
        try
        {
            var payloadBytes = DecodeBase64Url(parts[1]);
            var signature = DecodeBase64Url(parts[2]);
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);
            if (!rsa.VerifyData(payloadBytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) return false;
            payload = JsonSerializer.Deserialize<RenewalPayload>(payloadBytes, JsonOptions) ?? new RenewalPayload();
            return !string.IsNullOrWhiteSpace(payload.InstallationId) && payload.ValidUntilUtc != default && payload.IssuedUtc != default && !string.IsNullOrWhiteSpace(payload.TokenId);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or JsonException) { return false; }
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '=');
        return Convert.FromBase64String(base64);
    }

    private sealed class LicenseState
    {
        public string InstallationId { get; set; } = string.Empty;
        public DateTimeOffset TrialStartedUtc { get; set; }
        public DateTimeOffset TrialEndsUtc { get; set; }
        public DateTimeOffset LastObservedUtc { get; set; }
        public string? RenewalCode { get; set; }
    }

    private sealed class RenewalPayload
    {
        public string InstallationId { get; set; } = string.Empty;
        public DateTimeOffset ValidUntilUtc { get; set; }
        public DateTimeOffset IssuedUtc { get; set; }
        public string TokenId { get; set; } = string.Empty;
    }
}
