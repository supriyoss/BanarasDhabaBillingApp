using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application;
using RestaurantPos.Domain;

namespace RestaurantPos.Infrastructure;

public sealed class AdministrationService(RestaurantDbContext db, PinHasher pinHasher) : IAdministrationService
{
    public async Task<IReadOnlyList<AppUser>> GetStaffAsync(CancellationToken cancellationToken = default) => await db.Users.OrderBy(x => x.DisplayName).ToListAsync(cancellationToken);
    public async Task<AppUser> AddStaffAsync(UserRole role, string pin, int performedByUserId, CancellationToken cancellationToken = default)
    {
        await EnsureAccountAdministratorAsync(performedByUserId, cancellationToken);
        if (pin.Length < 4) throw new InvalidOperationException("Enter a PIN of at least four digits.");
        if (role is not (UserRole.Manager or UserRole.Cashier)) throw new InvalidOperationException("Only Manager and Cashier role accounts can be created.");
        if (await db.Users.AnyAsync(x => x.Role == role, cancellationToken)) throw new InvalidOperationException($"The {role} account already exists.");
        var displayName = role.ToString();
        var user = new AppUser { DisplayName = displayName, PinHash = pinHasher.Hash(pin), Role = role };
        db.Users.Add(user); db.AuditEntries.Add(new AuditEntry { UserId = performedByUserId, Action = AuditAction.Created, EntityType = "User", EntityId = displayName, Detail = $"Created {role} staff account." });
        await db.SaveChangesAsync(cancellationToken); return user;
    }
    public async Task ChangePinAsync(int userId, string currentPin, string newPin, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FindAsync([userId], cancellationToken) ?? throw new InvalidOperationException("Staff account was not found.");
        if (!pinHasher.Verify(currentPin, user.PinHash)) throw new InvalidOperationException("The current PIN is not correct.");
        if (newPin.Length < 4) throw new InvalidOperationException("The new PIN must contain at least four digits.");
        user.PinHash = pinHasher.Hash(newPin); db.AuditEntries.Add(new AuditEntry { UserId = userId, Action = AuditAction.Updated, EntityType = "User", EntityId = user.Id.ToString(), Detail = "Changed own PIN." });
        await db.SaveChangesAsync(cancellationToken);
    }
    public async Task ResetStaffPinAsync(int userId, string newPin, int performedByUserId, CancellationToken cancellationToken = default)
    {
        await EnsureAccountPinManagerAsync(performedByUserId, cancellationToken);
        if (userId == performedByUserId) throw new InvalidOperationException("Use the Change my PIN section to update your own PIN.");
        if (newPin.Length < 4) throw new InvalidOperationException("The new PIN must contain at least four digits.");
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId && x.IsActive, cancellationToken) ?? throw new InvalidOperationException("Select an active staff account.");
        if (user.Role == UserRole.Admin) throw new InvalidOperationException("Administrator account PINs cannot be reset from this screen.");
        user.PinHash = pinHasher.Hash(newPin);
        db.AuditEntries.Add(new AuditEntry { UserId = performedByUserId, Action = AuditAction.Updated, EntityType = "User", EntityId = user.Id.ToString(), Detail = $"Reset PIN for {user.Role} account '{user.DisplayName}'." });
        await db.SaveChangesAsync(cancellationToken);
    }
    public async Task<IReadOnlyList<MenuCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default) => await db.MenuCategories.Where(x => x.IsActive).OrderBy(x => x.SortOrder).ToListAsync(cancellationToken);
    public async Task<IReadOnlyList<MenuItem>> GetActiveMenuItemsAsync(CancellationToken cancellationToken = default) => await db.MenuItems.Include(x => x.MenuCategory).Where(x => x.IsActive).OrderBy(x => x.MenuCategory!.SortOrder).ThenBy(x => x.SortOrder).ToListAsync(cancellationToken);
    public async Task<MenuItem> AddMenuItemAsync(int categoryId, string name, decimal price, int performedByUserId, CancellationToken cancellationToken = default)
    {
        await EnsureRestaurantManagerAsync(performedByUserId, cancellationToken);
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Enter a menu item name.");
        if (price <= 0) throw new InvalidOperationException("Menu item price must be greater than 0.");
        if (!await db.MenuCategories.AnyAsync(x => x.Id == categoryId && x.IsActive, cancellationToken)) throw new InvalidOperationException("Select an active category.");
        var item = new MenuItem { MenuCategoryId = categoryId, Name = name, UnitPrice = price, GstRate = 0, SortOrder = await db.MenuItems.Where(x => x.MenuCategoryId == categoryId).CountAsync(cancellationToken) + 1 };
        db.MenuItems.Add(item); db.AuditEntries.Add(new AuditEntry { UserId = performedByUserId, Action = AuditAction.Created, EntityType = "MenuItem", EntityId = name, Detail = "Added menu item." });
        await db.SaveChangesAsync(cancellationToken); return item;
    }
    public async Task<MenuItem> UpdateMenuItemAsync(int menuItemId, int categoryId, string name, decimal price, int performedByUserId, CancellationToken cancellationToken = default)
    {
        await EnsureRestaurantManagerAsync(performedByUserId, cancellationToken);
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Enter a menu item name.");
        if (price <= 0) throw new InvalidOperationException("Menu item price must be greater than 0.");
        if (!await db.MenuCategories.AnyAsync(x => x.Id == categoryId && x.IsActive, cancellationToken)) throw new InvalidOperationException("Select an active category.");
        var item = await db.MenuItems.SingleOrDefaultAsync(x => x.Id == menuItemId && x.IsActive, cancellationToken) ?? throw new InvalidOperationException("Select an active menu item to edit.");
        item.MenuCategoryId = categoryId; item.Name = name; item.UnitPrice = price;
        db.AuditEntries.Add(new AuditEntry { UserId = performedByUserId, Action = AuditAction.Updated, EntityType = "MenuItem", EntityId = item.Id.ToString(), Detail = $"Updated menu item to '{name}' at {price:N2}." });
        await db.SaveChangesAsync(cancellationToken); return item;
    }
    public async Task<int> DeactivateMenuItemsAsync(IReadOnlyCollection<int> menuItemIds, int performedByUserId, CancellationToken cancellationToken = default)
    {
        await EnsureRestaurantManagerAsync(performedByUserId, cancellationToken);
        var ids = menuItemIds.Distinct().ToArray();
        if (ids.Length == 0) throw new InvalidOperationException("Select one or more menu items to remove.");
        var items = await db.MenuItems.Where(x => ids.Contains(x.Id) && x.IsActive).ToListAsync(cancellationToken);
        if (items.Count == 0) throw new InvalidOperationException("Select one or more active menu items to remove.");
        foreach (var item in items)
        {
            item.IsActive = false;
            db.AuditEntries.Add(new AuditEntry { UserId = performedByUserId, Action = AuditAction.Updated, EntityType = "MenuItem", EntityId = item.Id.ToString(), Detail = $"Removed menu item '{item.Name}' from the active menu." });
        }
        await db.SaveChangesAsync(cancellationToken); return items.Count;
    }
    public async Task<int> DeactivateStaffAccountsAsync(IReadOnlyCollection<int> userIds, int performedByUserId, CancellationToken cancellationToken = default)
    {
        await EnsureRestaurantManagerAsync(performedByUserId, cancellationToken);
        var ids = userIds.Distinct().ToArray();
        if (ids.Length == 0) throw new InvalidOperationException("Select one or more staff accounts to deactivate.");
        if (ids.Contains(performedByUserId)) throw new InvalidOperationException("You cannot deactivate your own account.");
        var users = await db.Users.Where(x => ids.Contains(x.Id) && x.IsActive).ToListAsync(cancellationToken);
        if (users.Count == 0) throw new InvalidOperationException("Select one or more active staff accounts to deactivate.");
        if (users.Any(x => x.Role == UserRole.Admin)) throw new InvalidOperationException("Administrator accounts cannot be deactivated from this screen.");
        foreach (var user in users)
        {
            user.IsActive = false;
            db.AuditEntries.Add(new AuditEntry { UserId = performedByUserId, Action = AuditAction.Updated, EntityType = "User", EntityId = user.Id.ToString(), Detail = $"Deactivated {user.Role} account '{user.DisplayName}'." });
        }
        await db.SaveChangesAsync(cancellationToken); return users.Count;
    }
    public async Task<RestaurantSettings> GetSettingsAsync(CancellationToken cancellationToken = default) => await db.RestaurantSettings.SingleAsync(x => x.Id == 1, cancellationToken);
    public async Task UpdateGstRateAsync(decimal gstRate, int performedByUserId, CancellationToken cancellationToken = default)
    {
        await EnsureRestaurantManagerAsync(performedByUserId, cancellationToken);
        if (gstRate < 0 || gstRate > 100) throw new InvalidOperationException("Enter a GST rate between 0 and 100.");
        var settings = await db.RestaurantSettings.SingleAsync(x => x.Id == 1, cancellationToken);
        settings.GstRate = gstRate; settings.UpdatedUtc = DateTime.UtcNow;
        db.AuditEntries.Add(new AuditEntry { UserId = performedByUserId, Action = AuditAction.Updated, EntityType = "RestaurantSettings", EntityId = "GST", Detail = $"Updated bill GST rate to {gstRate:N2}%." });
        await db.SaveChangesAsync(cancellationToken);
    }
    public async Task UpdateReceiptPaperWidthAsync(ReceiptPaperWidth paperWidth, int performedByUserId, CancellationToken cancellationToken = default)
    {
        await EnsureRestaurantManagerAsync(performedByUserId, cancellationToken);
        if (paperWidth is not (ReceiptPaperWidth.Mm58 or ReceiptPaperWidth.Mm80)) throw new InvalidOperationException("Choose either 58 mm or 80 mm receipt paper.");
        var settings = await db.RestaurantSettings.SingleAsync(x => x.Id == 1, cancellationToken);
        settings.ReceiptPaperWidthMm = (int)paperWidth;
        settings.UpdatedUtc = DateTime.UtcNow;
        db.AuditEntries.Add(new AuditEntry { UserId = performedByUserId, Action = AuditAction.Updated, EntityType = "RestaurantSettings", EntityId = "ReceiptPaper", Detail = $"Updated physical receipt paper width to {(int)paperWidth} mm." });
        await db.SaveChangesAsync(cancellationToken);
    }
    public async Task<IReadOnlyList<Order>> GetOrderHistoryAsync(DateTime fromDate, CancellationToken cancellationToken = default)
    {
        var start = DateTime.SpecifyKind(fromDate.Date, DateTimeKind.Local).ToUniversalTime();
        var end = start.AddDays(1);
        return await db.Orders.Include(x => x.DiningTable).Include(x => x.CreatedByUser).Where(x => x.Status == OrderStatus.Paid && x.ClosedUtc >= start && x.ClosedUtc < end).OrderByDescending(x => x.ClosedUtc).ToListAsync(cancellationToken);
    }

    private async Task EnsureRestaurantManagerAsync(int userId, CancellationToken cancellationToken)
    {
        if (!await db.Users.AnyAsync(x => x.Id == userId && x.IsActive && x.Role == UserRole.Manager, cancellationToken))
            throw new InvalidOperationException("Only the restaurant manager can change restaurant settings.");
    }
    private async Task EnsureAccountAdministratorAsync(int userId, CancellationToken cancellationToken)
    {
        if (!await db.Users.AnyAsync(x => x.Id == userId && x.IsActive && (x.Role == UserRole.Manager || x.Role == UserRole.Admin), cancellationToken))
            throw new InvalidOperationException("Only a manager or administrator can create role accounts.");
    }
    private async Task EnsureAccountPinManagerAsync(int userId, CancellationToken cancellationToken)
    {
        if (!await db.Users.AnyAsync(x => x.Id == userId && x.IsActive && (x.Role == UserRole.Manager || x.Role == UserRole.Admin), cancellationToken))
            throw new InvalidOperationException("Only a manager or administrator can reset account PINs.");
    }
}
