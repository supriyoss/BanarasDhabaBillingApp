using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application;
using RestaurantPos.Domain;

namespace RestaurantPos.Infrastructure;

public sealed class PinHasher
{
    private const int Iterations = 100_000;
    public string Hash(string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(pin, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }
    public bool Verify(string pin, string stored)
    {
        var parts = stored.Split('.');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations)) return false;
        try { var actual = Rfc2898DeriveBytes.Pbkdf2(pin, Convert.FromBase64String(parts[1]), iterations, HashAlgorithmName.SHA256, 32); return CryptographicOperations.FixedTimeEquals(actual, Convert.FromBase64String(parts[2])); }
        catch (FormatException) { return false; }
    }
}

public sealed class AuthenticationService(RestaurantDbContext db, PinHasher hasher) : IAuthenticationService
{
    public async Task<IReadOnlyList<AppUser>> GetActiveUsersAsync(CancellationToken cancellationToken = default) => await db.Users.Where(x => x.IsActive).OrderBy(x => x.DisplayName).ToListAsync(cancellationToken);
    public async Task<AppUser?> AuthenticateAsync(int userId, string pin, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId && x.IsActive, cancellationToken);
        if (user is null || !hasher.Verify(pin, user.PinHash)) return null;
        db.AuditEntries.Add(new AuditEntry { UserId = user.Id, Action = AuditAction.Login, EntityType = "User", EntityId = user.Id.ToString(), Detail = "Successful PIN login." });
        await db.SaveChangesAsync(cancellationToken);
        return user;
    }
}
