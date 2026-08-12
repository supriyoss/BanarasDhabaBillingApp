using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Domain;
using RestaurantPos.Infrastructure;
using Xunit;

namespace RestaurantPos.Tests;

public sealed class FloorPlanServiceTests
{
    [Fact]
    public async Task Manager_CanCreateLayoutSectionAndPositionedTable()
    {
        await using var fixture = await Fixture.CreateAsync();
        var layout = await fixture.Service.AddLayoutAsync("Rooftop", fixture.ManagerId);
        var section = await fixture.Service.AddSectionAsync(layout.Id, "Window Side", fixture.ManagerId);
        var table = await fixture.Service.AddTableAsync(layout.Id, section.Id, "R1", 6, 3, 2, TableShape.Rectangle, fixture.ManagerId);

        Assert.Equal(layout.Id, table.FloorLayoutId);
        Assert.Equal(section.Id, table.FloorSectionId);
        Assert.Equal((3, 2, 6, TableShape.Rectangle), (table.GridX, table.GridY, table.Capacity, table.Shape));
    }

    [Fact]
    public async Task Server_CannotEditFloorPlan()
    {
        await using var fixture = await Fixture.CreateAsync();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.AddLayoutAsync("Private", fixture.ServerId));
        Assert.Contains("restaurant manager", error.Message);
    }

    [Fact]
    public async Task Administrator_CannotEditRestaurantFloorPlan()
    {
        await using var fixture = await Fixture.CreateAsync();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.AddLayoutAsync("Application Floor", fixture.AdministratorId));
        Assert.Contains("restaurant manager", error.Message);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly RestaurantDbContext db;
        public FloorPlanService Service { get; }
        public int ManagerId { get; private set; }
        public int ServerId { get; private set; }
        public int AdministratorId { get; private set; }
        private Fixture(SqliteConnection connection, RestaurantDbContext db) { this.connection = connection; this.db = db; Service = new FloorPlanService(db); }
        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
            var db = new RestaurantDbContext(new DbContextOptionsBuilder<RestaurantDbContext>().UseSqlite(connection).Options); await db.Database.EnsureCreatedAsync();
            var manager = new AppUser { DisplayName = "Manager", PinHash = "test", Role = UserRole.Manager };
            var server = new AppUser { DisplayName = "Server", PinHash = "test", Role = UserRole.Server };
            var administrator = new AppUser { DisplayName = "Administrator", PinHash = "test", Role = UserRole.Admin };
            db.Users.AddRange(manager, server, administrator); await db.SaveChangesAsync();
            return new Fixture(connection, db) { ManagerId = manager.Id, ServerId = server.Id, AdministratorId = administrator.Id };
        }
        public async ValueTask DisposeAsync() { await db.DisposeAsync(); await connection.DisposeAsync(); }
    }
}
