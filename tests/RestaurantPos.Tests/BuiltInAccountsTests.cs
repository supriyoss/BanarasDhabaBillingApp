using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RestaurantPos.Domain;
using RestaurantPos.Infrastructure;
using Xunit;

namespace RestaurantPos.Tests;

public sealed class BuiltInAccountsTests
{
    [Fact]
    public async Task Initialization_CreatesBuiltInManagerWithConfiguredPin()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RestaurantDbContext>().UseSqlite(connection).Options;
        await using var db = new RestaurantDbContext(options);
        var hasher = new PinHasher();

        await new DatabaseInitializer(db, hasher).InitializeAsync();

        var manager = await db.Users.SingleAsync(x => x.DisplayName == "Manager");
        Assert.Equal(UserRole.Manager, manager.Role);
        Assert.True(manager.IsActive);
        Assert.True(hasher.Verify("9231", manager.PinHash));
    }

    [Fact]
    public async Task LegacyTableAndHeldOrder_AreNormalizedWithoutDataLoss()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RestaurantDbContext>().UseSqlite(connection).Options;
        await using var db = new RestaurantDbContext(options);
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync("INSERT INTO Users (DisplayName, PinHash, Role, IsActive, CreatedUtc) VALUES ('Legacy Staff','test',3,1,'2026-08-12')");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO DiningTables (Name, Capacity, IsActive) VALUES ('Legacy Table',4,1)");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO RestaurantSettings (Id, GstRate, UpdatedUtc) VALUES (1,5,'2026-08-12')");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO Orders (InvoiceNumber, Type, Status, DiningTableId, CreatedByUserId, OpenedUtc, ClosedUtc, Notes, DiscountAmount, TaxAmount, GrandTotal, DiscountType, DiscountValue, GstRate, ServerName) VALUES ('LEGACY-1',0,1,1,1,'2026-08-12',NULL,NULL,0,0,0,0,0,5,'Legacy Staff')");

        await new DatabaseInitializer(db, new PinHasher()).InitializeAsync();

        var table = await db.DiningTables.SingleAsync(x => x.Name == "Legacy Table");
        var order = await db.Orders.SingleAsync(x => x.InvoiceNumber == "LEGACY-1");
        Assert.NotNull(table.FloorLayoutId);
        Assert.Equal(OrderStatus.Open, order.Status);
        Assert.True(await db.Users.AnyAsync(x => x.DisplayName == "Manager" && x.Role == UserRole.Manager));
    }
}
