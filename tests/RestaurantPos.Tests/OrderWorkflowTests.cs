using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.IO;
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
    public async Task ServerName_IsOptionalAndIsNotReplacedWithAccountName()
    {
        await using var fixture = await WorkflowFixture.CreateAsync();

        var order = await fixture.Workflow.StartWithMenuItemAsync(OrderType.DineIn, fixture.TableId, fixture.MenuItemId, PreparationMode.DineIn, fixture.UserId, string.Empty);

        Assert.Equal(string.Empty, order.ServerName);
    }

    [Fact]
    public async Task OpenOrder_ServerNameCanBeUpdatedOrCleared()
    {
        await using var fixture = await WorkflowFixture.CreateAsync();
        var order = await fixture.Workflow.StartWithMenuItemAsync(OrderType.DineIn, fixture.TableId, fixture.MenuItemId, PreparationMode.DineIn, fixture.UserId, string.Empty);

        order = await fixture.Workflow.SetServerNameAsync(order.Id, "  Amit  ", fixture.UserId);
        Assert.Equal("Amit", order.ServerName);

        order = await fixture.Workflow.SetServerNameAsync(order.Id, string.Empty, fixture.UserId);
        Assert.Equal(string.Empty, order.ServerName);
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
    public async Task LegacyEmptyTableOrders_DoNotOccupyOrReopenTable1()
    {
        await using var fixture = await WorkflowFixture.CreateAsync();
        fixture.Db.Orders.AddRange(
            fixture.EmptyOrder("EMPTY-1", OrderType.DineIn, fixture.TableId),
            fixture.EmptyOrder("EMPTY-2", OrderType.DineIn, fixture.TableId));
        await fixture.Db.SaveChangesAsync();

        Assert.Null(await fixture.Workflow.FindActiveTableOrderAsync(fixture.TableId, fixture.UserId));
        var table = (await fixture.FloorPlans.GetLiveFloorPlansAsync()).Single().Tables.Single(x => x.Id == fixture.TableId);
        Assert.Equal("Available", table.State);
    }

    [Fact]
    public async Task RepeatedTable1CancelCycles_NeverBindTable2OrTakeaway()
    {
        await using var fixture = await WorkflowFixture.CreateAsync();
        var otherTableOrder = await fixture.Workflow.StartWithMenuItemAsync(OrderType.DineIn, fixture.Table2Id, fixture.MenuItemId, PreparationMode.DineIn, fixture.UserId, "Cashier");
        var takeaway = await fixture.Workflow.StartWithMenuItemAsync(OrderType.Takeaway, null, fixture.MenuItemId, PreparationMode.Packed, fixture.UserId, "Cashier");

        for (var cycle = 0; cycle < 3; cycle++)
        {
            var table1 = await fixture.Workflow.StartWithMenuItemAsync(OrderType.DineIn, fixture.TableId, fixture.MenuItemId, PreparationMode.DineIn, fixture.UserId, "Cashier");
            Assert.Equal(fixture.TableId, (await fixture.Workflow.FindActiveTableOrderAsync(fixture.TableId, fixture.UserId))!.DiningTableId);
            await fixture.Workflow.CancelAsync(table1.Id, fixture.UserId);
            Assert.Null(await fixture.Workflow.FindActiveTableOrderAsync(fixture.TableId, fixture.UserId));
        }

        Assert.Equal(otherTableOrder.Id, (await fixture.Workflow.FindActiveTableOrderAsync(fixture.Table2Id, fixture.UserId))!.Id);
        Assert.Equal(OrderType.Takeaway, (await fixture.Db.Orders.SingleAsync(x => x.Id == takeaway.Id)).Type);
    }

    [Fact]
    public async Task CompletedAndCancelledOrders_DoNotOccupyTable1()
    {
        await using var fixture = await WorkflowFixture.CreateAsync();
        var cancelled = await fixture.Workflow.StartWithMenuItemAsync(OrderType.DineIn, fixture.TableId, fixture.MenuItemId, PreparationMode.DineIn, fixture.UserId, "Cashier");
        await fixture.Workflow.CancelAsync(cancelled.Id, fixture.UserId);
        var paid = await fixture.Workflow.StartWithMenuItemAsync(OrderType.DineIn, fixture.TableId, fixture.MenuItemId, PreparationMode.DineIn, fixture.UserId, "Cashier");
        await fixture.Workflow.TakePaymentAsync(paid.Id, PaymentMethod.Cash, paid.GrandTotal, fixture.UserId);

        Assert.Null(await fixture.Workflow.FindActiveTableOrderAsync(fixture.TableId, fixture.UserId));
        Assert.Equal("Available", (await fixture.FloorPlans.GetLiveFloorPlansAsync()).Single().Tables.Single(x => x.Id == fixture.TableId).State);
    }

    [Fact]
    public async Task SqliteReload_PreservesExactTableOrderAssociation()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"restaurant-pos-table-association-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<RestaurantDbContext>().UseSqlite($"Data Source={databasePath};Pooling=False").Options;
            int userId, table1Id, table2Id, menuItemId, table2OrderId;
            await using (var db = new RestaurantDbContext(options))
            {
                await db.Database.EnsureCreatedAsync();
                var user = new AppUser { DisplayName = "Cashier", PinHash = "test", Role = UserRole.Cashier };
                var layout = new FloorLayout { Name = "Main Floor", IsDefault = true };
                var table1 = new DiningTable { Name = "Table 1", Capacity = 4, FloorLayout = layout };
                var table2 = new DiningTable { Name = "Table 2", Capacity = 4, FloorLayout = layout, GridX = 1 };
                var category = new MenuCategory { Name = "Mains" };
                var item = new MenuItem { Name = "Meal", UnitPrice = 100, MenuCategory = category };
                db.AddRange(user, layout, table1, table2, category, item, new RestaurantSettings { Id = 1, GstRate = 5 }); await db.SaveChangesAsync();
                var workflow = new OrderWorkflow(db, new OrderCalculator());
                var table2Order = await workflow.StartWithMenuItemAsync(OrderType.DineIn, table2.Id, item.Id, PreparationMode.DineIn, user.Id, "Cashier");
                userId = user.Id; table1Id = table1.Id; table2Id = table2.Id; menuItemId = item.Id; table2OrderId = table2Order.Id;
            }

            await using (var db = new RestaurantDbContext(options))
            {
                var workflow = new OrderWorkflow(db, new OrderCalculator());
                Assert.Null(await workflow.FindActiveTableOrderAsync(table1Id, userId));
                Assert.Equal(table2OrderId, (await workflow.FindActiveTableOrderAsync(table2Id, userId))!.Id);
                var table1Order = await workflow.StartWithMenuItemAsync(OrderType.DineIn, table1Id, menuItemId, PreparationMode.DineIn, userId, "Cashier");
                await workflow.CancelAsync(table1Order.Id, userId);
            }

            await using (var db = new RestaurantDbContext(options))
                Assert.Null(await new OrderWorkflow(db, new OrderCalculator()).FindActiveTableOrderAsync(table1Id, userId));
        }
        finally { if (File.Exists(databasePath)) File.Delete(databasePath); }
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
    public async Task ChangingPreparationMode_PreservesTotalAndTableOccupancy()
    {
        await using var fixture = await WorkflowFixture.CreateAsync();
        var order = await fixture.Workflow.StartWithMenuItemAsync(OrderType.DineIn, fixture.TableId, fixture.MenuItemId, PreparationMode.DineIn, fixture.UserId, "Cashier");
        var total = order.GrandTotal;

        order = await fixture.Workflow.SetLinePreparationModeAsync(order.Id, order.Lines.Single().Id, PreparationMode.Packed, fixture.UserId);

        Assert.Equal(PreparationMode.Packed, order.Lines.Single().PreparationMode);
        Assert.Equal(total, order.GrandTotal);
        Assert.NotNull(await fixture.Workflow.FindActiveTableOrderAsync(fixture.TableId, fixture.UserId));
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

    [Fact]
    public async Task FirstKot_CapturesCompletePriceFreeKitchenSnapshot()
    {
        await using var fixture = await WorkflowFixture.CreateAsync();
        var order = await fixture.Workflow.StartWithMenuItemAsync(OrderType.DineIn, fixture.TableId, fixture.MenuItemId, PreparationMode.DineIn, fixture.UserId, "Amit");
        order = await fixture.Workflow.AddMenuItemAsync(order.Id, fixture.MenuItemId, fixture.UserId);

        var kot = await fixture.Workflow.CreateKitchenOrderTicketAsync(order.Id, fixture.UserId);

        Assert.Equal(1, kot.SequenceNumber);
        Assert.False(kot.IsSupplementary);
        Assert.StartsWith("KOT-", kot.TicketNumber);
        Assert.Equal(2m, kot.Lines.Single().Quantity);
        Assert.Equal("Meal", kot.Lines.Single().ItemName);
        Assert.Equal(PreparationMode.DineIn, kot.Lines.Single().PreparationMode);
    }

    [Fact]
    public async Task SupplementaryKot_ContainsOnlyQuantityAddedSinceEarlierTickets()
    {
        await using var fixture = await WorkflowFixture.CreateAsync();
        var order = await fixture.Workflow.StartWithMenuItemAsync(OrderType.Takeaway, null, fixture.MenuItemId, PreparationMode.Packed, fixture.UserId, "Staff");
        Assert.True(await fixture.Workflow.HasPendingKitchenOrderTicketItemsAsync(order.Id, fixture.UserId));
        var first = await fixture.Workflow.CreateKitchenOrderTicketAsync(order.Id, fixture.UserId);
        Assert.False(await fixture.Workflow.HasPendingKitchenOrderTicketItemsAsync(order.Id, fixture.UserId));
        order = await fixture.Workflow.AddMenuItemAsync(order.Id, fixture.MenuItemId, fixture.UserId);
        Assert.True(await fixture.Workflow.HasPendingKitchenOrderTicketItemsAsync(order.Id, fixture.UserId));

        var supplementary = await fixture.Workflow.CreateKitchenOrderTicketAsync(order.Id, fixture.UserId);

        Assert.Equal(2, supplementary.SequenceNumber);
        Assert.True(supplementary.IsSupplementary);
        Assert.Equal(1m, supplementary.Lines.Single().Quantity);
        Assert.NotEqual(first.TicketNumber, supplementary.TicketNumber);
        Assert.False(await fixture.Workflow.HasPendingKitchenOrderTicketItemsAsync(order.Id, fixture.UserId));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Workflow.CreateKitchenOrderTicketAsync(order.Id, fixture.UserId));
        Assert.Contains("no new or increased items", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LatestKot_CanBeRetrievedAndSuccessfulPrintsAreAudited()
    {
        await using var fixture = await WorkflowFixture.CreateAsync();
        var order = await fixture.Workflow.StartWithMenuItemAsync(OrderType.DineIn, fixture.TableId, fixture.MenuItemId, PreparationMode.DineIn, fixture.UserId, "Staff");
        var ticket = await fixture.Workflow.CreateKitchenOrderTicketAsync(order.Id, fixture.UserId);

        await fixture.Workflow.RecordKitchenOrderTicketPrintAsync(ticket.Id, fixture.UserId);
        var latest = await fixture.Workflow.GetLatestKitchenOrderTicketAsync(order.Id, fixture.UserId);

        Assert.NotNull(latest);
        Assert.Equal(ticket.Id, latest.Id);
        Assert.Equal(1, latest.PrintCount);
        Assert.NotNull(latest.LastPrintedUtc);
        Assert.Contains(await fixture.Db.AuditEntries.ToListAsync(), x => x.Action == AuditAction.KitchenTicketPrinted && x.EntityId == ticket.TicketNumber);
    }

    [Fact]
    public async Task TakeawayKot_RemainsAvailableAfterOrderMovesToOpenTakeawaysAndReopens()
    {
        await using var fixture = await WorkflowFixture.CreateAsync();
        var order = await fixture.Workflow.StartWithMenuItemAsync(OrderType.Takeaway, null, fixture.MenuItemId, PreparationMode.Packed, fixture.UserId, "Staff");
        var ticket = await fixture.Workflow.CreateKitchenOrderTicketAsync(order.Id, fixture.UserId);
        await fixture.Workflow.HoldTakeawayAsync(order.Id, fixture.UserId);

        Assert.Equal(order.Id, (await fixture.Workflow.GetOpenTakeawayOrdersAsync()).Single().Id);
        var reopened = await fixture.Workflow.OpenTakeawayAsync(order.Id, fixture.UserId);
        var latest = await fixture.Workflow.GetLatestKitchenOrderTicketAsync(reopened.Id, fixture.UserId);

        Assert.Equal(ticket.Id, latest?.Id);
        Assert.False(await fixture.Workflow.HasPendingKitchenOrderTicketItemsAsync(reopened.Id, fixture.UserId));
    }

    private sealed class WorkflowFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public RestaurantDbContext Db { get; }
        public OrderWorkflow Workflow { get; }
        public FloorPlanService FloorPlans { get; }
        public int UserId { get; private set; }
        public int TableId { get; private set; }
        public int Table2Id { get; private set; }
        public int MenuItemId { get; private set; }

        private WorkflowFixture(SqliteConnection connection, RestaurantDbContext db)
        {
            this.connection = connection;
            Db = db;
            Workflow = new OrderWorkflow(db, new OrderCalculator());
            FloorPlans = new FloorPlanService(db);
        }

        public static async Task<WorkflowFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new RestaurantDbContext(new DbContextOptionsBuilder<RestaurantDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var user = new AppUser { DisplayName = "Cashier", PinHash = "test", Role = UserRole.Cashier };
            var layout = new FloorLayout { Name = "Main Floor", IsDefault = true };
            var table = new DiningTable { Name = "Table 1", Capacity = 4, FloorLayout = layout };
            var table2 = new DiningTable { Name = "Table 2", Capacity = 4, FloorLayout = layout, GridX = 1 };
            var category = new MenuCategory { Name = "Mains", SortOrder = 1 };
            var item = new MenuItem { Name = "Meal", UnitPrice = 100m, MenuCategory = category };
            db.Users.Add(user);
            db.FloorLayouts.Add(layout); db.DiningTables.AddRange(table, table2);
            db.MenuCategories.Add(category); db.MenuItems.Add(item);
            db.RestaurantSettings.Add(new RestaurantSettings { Id = 1, GstRate = 5m });
            await db.SaveChangesAsync();
            return new WorkflowFixture(connection, db) { UserId = user.Id, TableId = table.Id, Table2Id = table2.Id, MenuItemId = item.Id };
        }

        public Order EmptyOrder(string invoice, OrderType type, int? tableId) => new() { InvoiceNumber = invoice, Type = type, Status = OrderStatus.Open, DiningTableId = tableId, CreatedByUserId = UserId, ServerName = string.Empty, GstRate = 5m };

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
