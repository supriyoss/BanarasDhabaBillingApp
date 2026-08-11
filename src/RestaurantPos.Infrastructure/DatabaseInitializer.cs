using Microsoft.EntityFrameworkCore;
using RestaurantPos.Domain;

namespace RestaurantPos.Infrastructure;

public sealed class DatabaseInitializer(RestaurantDbContext db, PinHasher pinHasher)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await db.Database.MigrateAsync(cancellationToken);
        if (!await db.RestaurantSettings.AnyAsync(cancellationToken)) { db.RestaurantSettings.Add(new RestaurantSettings { Id = 1, GstRate = 5m }); await db.SaveChangesAsync(cancellationToken); }
        var administrator = await db.Users.SingleOrDefaultAsync(x => x.DisplayName == "Administrator", cancellationToken);
        if (administrator is null)
        {
            db.Users.Add(new AppUser { DisplayName = "Administrator", PinHash = pinHasher.Hash("1234"), Role = UserRole.Admin });
            await db.SaveChangesAsync(cancellationToken);
        }
        else if (administrator.PinHash == "REPLACE_DURING_SETUP")
        {
            administrator.PinHash = pinHasher.Hash("1234");
            await db.SaveChangesAsync(cancellationToken);
        }
        if (await db.MenuCategories.AnyAsync(cancellationToken)) return;
        var starters = new MenuCategory { Name = "Starters", SortOrder = 1 };
        var mains = new MenuCategory { Name = "Mains", SortOrder = 2 };
        var beverages = new MenuCategory { Name = "Beverages", SortOrder = 3 };
        db.MenuCategories.AddRange(starters, mains, beverages);
        db.MenuItems.AddRange(new MenuItem { MenuCategory = starters, Name = "Paneer Tikka", UnitPrice = 240, GstRate = 5, SortOrder = 1 }, new MenuItem { MenuCategory = mains, Name = "Veg Biryani", UnitPrice = 220, GstRate = 5, SortOrder = 1 }, new MenuItem { MenuCategory = beverages, Name = "Masala Chai", UnitPrice = 40, GstRate = 5, SortOrder = 1 });
        db.DiningTables.AddRange(Enumerable.Range(1, 8).Select(n => new DiningTable { Name = $"Table {n}", Capacity = 4 }));
        await db.SaveChangesAsync(cancellationToken);
    }
}
