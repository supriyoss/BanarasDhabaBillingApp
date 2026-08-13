using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Domain;
using RestaurantPos.Infrastructure;
using Xunit;

namespace RestaurantPos.Tests;

public sealed class MenuManagementTests
{
    [Fact]
    public async Task MenuItemPrice_MustBeGreaterThanZero()
    {
        await using var fixture = await Fixture.CreateAsync();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.AddMenuItemAsync(fixture.CategoryId, "Free item", 0, fixture.ManagerId));
        Assert.Contains("greater than 0", error.Message);
    }

    [Fact]
    public async Task Manager_CanEditMenuItemNameCategoryAndPrice()
    {
        await using var fixture = await Fixture.CreateAsync();
        var item = await fixture.Service.AddMenuItemAsync(fixture.CategoryId, "Panner", 200, fixture.ManagerId);
        var updated = await fixture.Service.UpdateMenuItemAsync(item.Id, fixture.OtherCategoryId, "Paneer", 240, fixture.ManagerId);
        Assert.Equal(("Paneer", 240m, fixture.OtherCategoryId), (updated.Name, updated.UnitPrice, updated.MenuCategoryId));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection; private readonly RestaurantDbContext db;
        public AdministrationService Service { get; } public int ManagerId { get; } public int CategoryId { get; } public int OtherCategoryId { get; }
        private Fixture(SqliteConnection connection, RestaurantDbContext db, int managerId, int categoryId, int otherCategoryId) { this.connection = connection; this.db = db; ManagerId = managerId; CategoryId = categoryId; OtherCategoryId = otherCategoryId; Service = new AdministrationService(db, new PinHasher()); }
        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
            var db = new RestaurantDbContext(new DbContextOptionsBuilder<RestaurantDbContext>().UseSqlite(connection).Options); await db.Database.EnsureCreatedAsync();
            var manager = new AppUser { DisplayName = "Manager", PinHash = "test", Role = UserRole.Manager };
            var first = new MenuCategory { Name = "Mains" }; var second = new MenuCategory { Name = "Starters" };
            db.Users.Add(manager); db.MenuCategories.AddRange(first, second); db.RestaurantSettings.Add(new RestaurantSettings { Id = 1 }); await db.SaveChangesAsync();
            return new Fixture(connection, db, manager.Id, first.Id, second.Id);
        }
        public async ValueTask DisposeAsync() { await db.DisposeAsync(); await connection.DisposeAsync(); }
    }
}
