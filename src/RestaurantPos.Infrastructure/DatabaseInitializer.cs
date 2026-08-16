using Microsoft.EntityFrameworkCore;
using RestaurantPos.Domain;

namespace RestaurantPos.Infrastructure;

public sealed class DatabaseInitializer(RestaurantDbContext db, PinHasher pinHasher)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await db.Database.MigrateAsync(cancellationToken);
        await db.Orders.Where(x => x.Status == OrderStatus.Held).ExecuteUpdateAsync(x => x.SetProperty(o => o.Status, OrderStatus.Open), cancellationToken);
        var changed = false;
        if (!await db.RestaurantSettings.AnyAsync(cancellationToken)) { db.RestaurantSettings.Add(new RestaurantSettings { Id = 1, GstRate = 5m, ReceiptPaperWidthMm = 80 }); changed = true; }

        var builtInUsers = await db.Users.Where(x => x.DisplayName == "Administrator" || x.DisplayName == "Manager").ToListAsync(cancellationToken);
        var administrator = builtInUsers.SingleOrDefault(x => x.DisplayName == "Administrator");
        if (administrator is null)
        {
            db.Users.Add(new AppUser { DisplayName = "Administrator", PinHash = pinHasher.Hash("1234"), Role = UserRole.Admin });
            changed = true;
        }
        else if (administrator.PinHash == "REPLACE_DURING_SETUP")
        {
            administrator.PinHash = pinHasher.Hash("1234");
            changed = true;
        }
        var manager = builtInUsers.SingleOrDefault(x => x.DisplayName == "Manager");
        if (manager is null)
        {
            db.Users.Add(new AppUser { DisplayName = "Manager", PinHash = pinHasher.Hash("9231"), Role = UserRole.Manager });
            changed = true;
        }
        else if (manager.PinHash == "REPLACE_DURING_SETUP")
        {
            manager.PinHash = pinHasher.Hash("9231"); manager.Role = UserRole.Manager; manager.IsActive = true;
            changed = true;
        }

        var defaultLayout = await db.FloorLayouts.OrderBy(x => x.SortOrder).FirstOrDefaultAsync(cancellationToken);
        if (defaultLayout is null)
        {
            defaultLayout = new FloorLayout { Name = "Main Floor", SortOrder = 1, IsDefault = true };
            db.FloorLayouts.Add(defaultLayout);
            changed = true;
        }
        var unplacedTables = await db.DiningTables.Where(x => x.FloorLayoutId == null).OrderBy(x => x.Id).ToListAsync(cancellationToken);
        for (var index = 0; index < unplacedTables.Count; index++)
        {
            unplacedTables[index].FloorLayout = defaultLayout;
            unplacedTables[index].GridX = index % 4;
            unplacedTables[index].GridY = index / 4;
            changed = true;
        }

        if (!await db.MenuCategories.AnyAsync(cancellationToken))
        {
            var starters = new MenuCategory { Name = "Starters", SortOrder = 1 };
            var mains = new MenuCategory { Name = "Mains", SortOrder = 2 };
            var beverages = new MenuCategory { Name = "Beverages", SortOrder = 3 };
            db.MenuCategories.AddRange(starters, mains, beverages);
            db.MenuItems.AddRange(new MenuItem { MenuCategory = starters, Name = "Paneer Tikka", UnitPrice = 240, GstRate = 5, SortOrder = 1 }, new MenuItem { MenuCategory = mains, Name = "Veg Biryani", UnitPrice = 220, GstRate = 5, SortOrder = 1 }, new MenuItem { MenuCategory = beverages, Name = "Masala Chai", UnitPrice = 40, GstRate = 5, SortOrder = 1 });
            db.DiningTables.AddRange(Enumerable.Range(1, 8).Select(n => new DiningTable { Name = $"Table {n}", Capacity = 4, FloorLayout = defaultLayout, GridX = (n - 1) % 4, GridY = (n - 1) / 4 }));
            changed = true;
        }
        if (changed) await db.SaveChangesAsync(cancellationToken);
    }
}
