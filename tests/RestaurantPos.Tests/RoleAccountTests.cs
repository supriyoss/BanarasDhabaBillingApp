using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application;
using RestaurantPos.Domain;
using RestaurantPos.Infrastructure;
using Xunit;

namespace RestaurantPos.Tests;

public sealed class RoleAccountTests
{
    [Theory]
    [InlineData(UserRole.Manager, "Manager")]
    [InlineData(UserRole.Cashier, "Cashier")]
    public async Task RoleAccount_RequiresNoPersonalName_AndPreventsDuplicates(UserRole role, string expectedName)
    {
        await using var fixture = await Fixture.CreateAsync(role == UserRole.Manager ? UserRole.Admin : UserRole.Manager);
        var account = await fixture.Service.AddStaffAsync(role, "4567", fixture.ActorId);
        Assert.Equal(expectedName, account.DisplayName);
        Assert.Equal(role, account.Role);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.AddStaffAsync(role, "8910", fixture.ActorId));
    }

    [Fact]
    public void RolePermissions_MatchOperationalBoundaries()
    {
        Assert.True(RolePermissions.CanUsePos(UserRole.Cashier));
        Assert.False(RolePermissions.CanManageRestaurant(UserRole.Cashier));
        Assert.True(RolePermissions.CanManageRestaurant(UserRole.Manager));
        Assert.False(RolePermissions.CanManageApplication(UserRole.Manager));
        Assert.True(RolePermissions.CanManageApplication(UserRole.Admin));
        Assert.False(RolePermissions.CanUsePos(UserRole.Admin));
    }

    [Fact]
    public async Task Administrator_CanResetAnotherAccountsPin()
    {
        await using var fixture = await Fixture.CreateAsync(UserRole.Admin);
        var account = await fixture.Service.AddStaffAsync(UserRole.Cashier, "4567", fixture.ActorId);

        await fixture.Service.ResetStaffPinAsync(account.Id, "8910", fixture.ActorId);

        Assert.True(new PinHasher().Verify("8910", account.PinHash));
    }

    [Fact]
    public async Task Manager_CanPersistPhysicalReceiptPaperWidth()
    {
        await using var fixture = await Fixture.CreateAsync(UserRole.Manager);

        await fixture.Service.UpdateReceiptPaperWidthAsync(ReceiptPaperWidth.Mm58, fixture.ActorId);

        Assert.Equal(58, (await fixture.Service.GetSettingsAsync()).ReceiptPaperWidthMm);
    }

    [Fact]
    public async Task PhysicalReceiptPaperWidth_RejectsUnsupportedValues()
    {
        await using var fixture = await Fixture.CreateAsync(UserRole.Manager);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.UpdateReceiptPaperWidthAsync((ReceiptPaperWidth)70, fixture.ActorId));
    }

    [Fact]
    public async Task Manager_CanConfigureSeparateReceiptAndKotPrinters()
    {
        await using var fixture = await Fixture.CreateAsync(UserRole.Manager);

        await fixture.Service.UpdatePrinterConfigurationAsync("Front Receipt", "Kitchen KOT", false, fixture.ActorId);

        var settings = await fixture.Service.GetSettingsAsync();
        Assert.Equal("Front Receipt", settings.ReceiptPrinterName);
        Assert.Equal("Kitchen KOT", settings.KitchenPrinterName);
        Assert.False(settings.UseSamePrinterForKitchen);
    }

    [Fact]
    public async Task SamePrinterConfiguration_RoutesKotToReceiptPrinter()
    {
        await using var fixture = await Fixture.CreateAsync(UserRole.Manager);

        await fixture.Service.UpdatePrinterConfigurationAsync("Thermal Printer", string.Empty, true, fixture.ActorId);

        var settings = await fixture.Service.GetSettingsAsync();
        Assert.Equal("Thermal Printer", settings.ReceiptPrinterName);
        Assert.Equal("Thermal Printer", settings.KitchenPrinterName);
        Assert.True(settings.UseSamePrinterForKitchen);
    }

    [Fact]
    public async Task PrinterConfiguration_RequiresEverySelectedDestination()
    {
        await using var fixture = await Fixture.CreateAsync(UserRole.Manager);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.UpdatePrinterConfigurationAsync(string.Empty, "Kitchen", false, fixture.ActorId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.UpdatePrinterConfigurationAsync("Receipt", string.Empty, false, fixture.ActorId));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly RestaurantDbContext db;
        public AdministrationService Service { get; }
        public int ActorId { get; }
        private Fixture(SqliteConnection connection, RestaurantDbContext db, int actorId) { this.connection = connection; this.db = db; ActorId = actorId; Service = new AdministrationService(db, new PinHasher()); }
        public static async Task<Fixture> CreateAsync(UserRole actorRole)
        {
            var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
            var db = new RestaurantDbContext(new DbContextOptionsBuilder<RestaurantDbContext>().UseSqlite(connection).Options); await db.Database.EnsureCreatedAsync();
            var actor = new AppUser { DisplayName = actorRole.ToString(), PinHash = "test", Role = actorRole };
            db.Users.Add(actor); db.RestaurantSettings.Add(new RestaurantSettings { Id = 1 }); await db.SaveChangesAsync();
            return new Fixture(connection, db, actor.Id);
        }
        public async ValueTask DisposeAsync() { await db.DisposeAsync(); await connection.DisposeAsync(); }
    }
}
