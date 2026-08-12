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

        var first = await fixture.Workflow.OpenTableAsync(fixture.TableId, fixture.UserId, "Staff");
        var second = await fixture.Workflow.OpenTableAsync(fixture.TableId, fixture.UserId, "Staff");

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await fixture.Db.Orders.CountAsync());
    }

    [Fact]
    public async Task OpenTable_ReopensLegacyHeldDineInOrder()
    {
        await using var fixture = await WorkflowFixture.CreateAsync();
        var order = await fixture.Workflow.OpenTableAsync(fixture.TableId, fixture.UserId, "Staff");
        order.Status = OrderStatus.Held;
        await fixture.Db.SaveChangesAsync();

        var reopened = await fixture.Workflow.OpenTableAsync(fixture.TableId, fixture.UserId, "Staff");

        Assert.Equal(order.Id, reopened.Id);
        Assert.Equal(OrderStatus.Open, reopened.Status);
    }

    [Fact]
    public async Task OpenTakeawayQueue_ContainsTakeawaysButNotDiningOrders()
    {
        await using var fixture = await WorkflowFixture.CreateAsync();
        var takeaway = await fixture.Workflow.CreateAsync(OrderType.Takeaway, null, fixture.UserId, "Staff");
        await fixture.Workflow.OpenTableAsync(fixture.TableId, fixture.UserId, "Staff");

        var queue = await fixture.Workflow.GetOpenTakeawayOrdersAsync();

        Assert.Single(queue);
        Assert.Equal(takeaway.Id, queue[0].Id);
    }

    [Fact]
    public async Task Payment_ClearsBillRequestAndReleasesDiningTable()
    {
        await using var fixture = await WorkflowFixture.CreateAsync();
        var order = await fixture.Workflow.OpenTableAsync(fixture.TableId, fixture.UserId, "Staff");
        order = await fixture.Workflow.AddMenuItemAsync(order.Id, fixture.MenuItemId, fixture.UserId);
        order = await fixture.Workflow.SetBillRequestedAsync(order.Id, true, fixture.UserId);

        var paid = await fixture.Workflow.TakePaymentAsync(order.Id, PaymentMethod.Cash, order.GrandTotal, fixture.UserId);
        var reopened = await fixture.Workflow.OpenTableAsync(fixture.TableId, fixture.UserId, "Staff");

        Assert.Equal(OrderStatus.Paid, paid.Status);
        Assert.False(paid.BillRequested);
        Assert.NotEqual(paid.Id, reopened.Id);
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
