using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application;
using RestaurantPos.Domain;
using RestaurantPos.Infrastructure;
using Xunit;

namespace RestaurantPos.Tests;

public sealed class OrderWorkflowTests
{
    [Fact]
    public async Task OpenTable_ReturnsExistingActiveOrderInsteadOfCreatingAnother()
    {
        await using var fixture = await WorkflowFixture.CreateAsync();

        var first = await fixture.Workflow.StartWithMenuItemAsync(OrderType.DineIn, fixture.TableId, fixture.MenuItemId, PreparationMode.DineIn, fixture.UserId, "Staff");
        var second = await fixture.Workflow.FindActiveTableOrderAsync(fixture.TableId, fixture.UserId);

        Assert.NotNull(second);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await fixture.Db.Orders.CountAsync());
    }

    [Fact]
    public async Task OpenTable_ReopensLegacyHeldDineInOrder()
    {
        await using var fixture = await WorkflowFixture.CreateAsync();
        var order = await fixture.Workflow.StartWithMenuItemAsync(OrderType.DineIn, fixture.TableId, fixture.MenuItemId, PreparationMode.DineIn, fixture.UserId, "Staff");
        order.Status = OrderStatus.Held;
        await fixture.Db.SaveChangesAsync();

        var reopened = await fixture.Workflow.FindActiveTableOrderAsync(fixture.TableId, fixture.UserId);

        Assert.NotNull(reopened);
        Assert.Equal(order.Id, reopened.Id);
        Assert.Equal(OrderStatus.Open, reopened.Status);
    }

    [Fact]
    public async Task OpenTakeawayQueue_ContainsTakeawaysButNotDiningOrders()
    {
        await using var fixture = await WorkflowFixture.CreateAsync();
        var takeaway = await fixture.Workflow.StartWithMenuItemAsync(OrderType.Takeaway, null, fixture.MenuItemId, PreparationMode.Packed, fixture.UserId, "Staff");
        takeaway = await fixture.Workflow.HoldTakeawayAsync(takeaway.Id, fixture.UserId);
        await fixture.Workflow.StartWithMenuItemAsync(OrderType.DineIn, fixture.TableId, fixture.MenuItemId, PreparationMode.DineIn, fixture.UserId, "Staff");

        var queue = await fixture.Workflow.GetOpenTakeawayOrdersAsync();

        Assert.Single(queue);
        Assert.Equal(takeaway.Id, queue[0].Id);
    }

    [Fact]
    public async Task Payment_ClearsBillRequestAndReleasesDiningTable()
    {
        await using var fixture = await WorkflowFixture.CreateAsync();
        var order = await fixture.Workflow.StartWithMenuItemAsync(OrderType.DineIn, fixture.TableId, fixture.MenuItemId, PreparationMode.DineIn, fixture.UserId, "Staff");

        var paid = await fixture.Workflow.TakePaymentAsync(order.Id, PaymentMethod.Cash, order.GrandTotal, fixture.UserId);
        var reopened = await fixture.Workflow.FindActiveTableOrderAsync(fixture.TableId, fixture.UserId);

        Assert.Equal(OrderStatus.Paid, paid.Status);
        Assert.Null(reopened);
    }

    [Fact]
    public async Task SelectingFreeTable_DoesNotCreateOrOccupyIt()
    {
        await using var fixture = await WorkflowFixture.CreateAsync();
        Assert.Null(await fixture.Workflow.FindActiveTableOrderAsync(fixture.TableId, fixture.UserId));
        Assert.Empty(await fixture.Db.Orders.ToListAsync());
    }

    [Fact]
    public async Task EmptyTakeawayNavigation_CreatesNothingAndQueueStaysEmpty()
    {
        await using var fixture = await WorkflowFixture.CreateAsync();
        Assert.Empty(await fixture.Workflow.GetOpenTakeawayOrdersAsync());
        Assert.Empty(await fixture.Db.Orders.ToListAsync());
    }

    [Fact]
    public async Task CancellingDiningOrder_ReleasesTable()
    {
        await using var fixture = await WorkflowFixture.CreateAsync();
        var order = await fixture.Workflow.StartWithMenuItemAsync(OrderType.DineIn, fixture.TableId, fixture.MenuItemId, PreparationMode.DineIn, fixture.UserId, "Staff");
        await fixture.Workflow.CancelAsync(order.Id, fixture.UserId);
        Assert.Null(await fixture.Workflow.FindActiveTableOrderAsync(fixture.TableId, fixture.UserId));
    }

    [Fact]
    public async Task DineInAndPackedLines_CoexistWithoutChangingTotal()
    {
        await using var fixture = await WorkflowFixture.CreateAsync();
        var order = await fixture.Workflow.StartWithMenuItemAsync(OrderType.DineIn, fixture.TableId, fixture.MenuItemId, PreparationMode.DineIn, fixture.UserId, "Staff");
        order = await fixture.Workflow.AddMenuItemAsync(order.Id, fixture.MenuItemId, PreparationMode.Packed, fixture.UserId);
        Assert.Equal(2, order.Lines.Count);
        Assert.Contains(order.Lines, x => x.PreparationMode == PreparationMode.DineIn);
        Assert.Contains(order.Lines, x => x.PreparationMode == PreparationMode.Packed);
        Assert.Equal(210m, order.GrandTotal);
        fixture.Db.ChangeTracker.Clear();
        var persisted = await fixture.Db.Orders.Include(x => x.Lines).SingleAsync(x => x.Id == order.Id);
        Assert.Equal(new[] { PreparationMode.DineIn, PreparationMode.Packed }, persisted.Lines.OrderBy(x => x.PreparationMode).Select(x => x.PreparationMode));
    }

    [Fact]
    public async Task MeaningfulTakeaway_AppearsOnlyAfterExplicitHold()
    {
        await using var fixture = await WorkflowFixture.CreateAsync();
        var order = await fixture.Workflow.StartWithMenuItemAsync(OrderType.Takeaway, null, fixture.MenuItemId, PreparationMode.Packed, fixture.UserId, "Staff");
        Assert.Empty(await fixture.Workflow.GetOpenTakeawayOrdersAsync());
        await fixture.Workflow.HoldTakeawayAsync(order.Id, fixture.UserId);
        Assert.Single(await fixture.Workflow.GetOpenTakeawayOrdersAsync());
    }

    [Fact]
    public async Task Takeaway_CanBePaidDirectlyWithoutQueueRoundTrip()
    {
        await using var fixture = await WorkflowFixture.CreateAsync();
        var order = await fixture.Workflow.StartWithMenuItemAsync(OrderType.Takeaway, null, fixture.MenuItemId, PreparationMode.Packed, fixture.UserId, "Staff");
        var paid = await fixture.Workflow.TakePaymentAsync(order.Id, PaymentMethod.Upi, order.GrandTotal, fixture.UserId);
        Assert.Equal(OrderStatus.Paid, paid.Status);
        Assert.Empty(await fixture.Workflow.GetOpenTakeawayOrdersAsync());
    }

    private sealed class WorkflowFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public RestaurantDbContext Db { get; }
        public OrderWorkflow Workflow { get; }
        public int UserId { get; private set; }
        public int TableId { get; private set; }
        public int MenuItemId { get; private set; }

        private WorkflowFixture(SqliteConnection connection, RestaurantDbContext db)
        {
            this.connection = connection;
            Db = db;
            Workflow = new OrderWorkflow(db, new OrderCalculator());
        }

        public static async Task<WorkflowFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new RestaurantDbContext(new DbContextOptionsBuilder<RestaurantDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var user = new AppUser { DisplayName = "Staff", PinHash = "test", Role = UserRole.Server };
            var table = new DiningTable { Name = "Table 1", Capacity = 4 };
            var category = new MenuCategory { Name = "Mains", SortOrder = 1 };
            var item = new MenuItem { Name = "Meal", UnitPrice = 100m, MenuCategory = category };
            db.Users.Add(user);
            db.DiningTables.Add(table);
            db.MenuCategories.Add(category); db.MenuItems.Add(item);
            db.RestaurantSettings.Add(new RestaurantSettings { Id = 1, GstRate = 5m });
            await db.SaveChangesAsync();
            return new WorkflowFixture(connection, db) { UserId = user.Id, TableId = table.Id, MenuItemId = item.Id };
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
