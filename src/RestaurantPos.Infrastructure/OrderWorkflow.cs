using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application;
using RestaurantPos.Domain;

namespace RestaurantPos.Infrastructure;

public sealed class OrderWorkflow(RestaurantDbContext db, IOrderCalculator calculator) : IOrderWorkflow
{
    public async Task<Order?> FindActiveTableOrderAsync(int tableId, int userId, CancellationToken cancellationToken = default)
    {
        await EnsureOperationalUserAsync(userId, cancellationToken);
        var tableExists = await db.DiningTables.AnyAsync(x => x.Id == tableId && x.IsActive, cancellationToken);
        if (!tableExists) throw new InvalidOperationException("This dining table is unavailable.");

        var existing = await db.Orders.Include(x => x.Lines).Include(x => x.Payments)
            .Where(x => x.Type == OrderType.DineIn && x.DiningTableId == tableId &&
                (x.Status == OrderStatus.Open || x.Status == OrderStatus.Held))
            .OrderByDescending(x => x.OpenedUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing?.Status == OrderStatus.Held)
        {
            existing.Status = OrderStatus.Open;
            Recalculate(existing);
            AddAudit(userId, AuditAction.Resumed, "Order", existing.InvoiceNumber, "Reopened from its dining table.");
            await db.SaveChangesAsync(cancellationToken);
        }
        return existing;
    }

    public async Task<Order> StartWithMenuItemAsync(OrderType type, int? tableId, int menuItemId, PreparationMode preparationMode, int userId, string serverName, CancellationToken cancellationToken = default)
    {
        if (type == OrderType.DineIn && tableId is int selectedTable && await db.Orders.AnyAsync(x => x.Type == OrderType.DineIn && x.DiningTableId == selectedTable && (x.Status == OrderStatus.Open || x.Status == OrderStatus.Held), cancellationToken))
            throw new InvalidOperationException("This table already has an active order. Reopen it from the floor plan.");
        var order = await CreateAsync(type, tableId, userId, serverName, cancellationToken);
        try { return await AddMenuItemAsync(order.Id, menuItemId, userId, cancellationToken, preparationMode); }
        catch { db.Orders.Remove(order); await db.SaveChangesAsync(cancellationToken); throw; }
    }

    private async Task<Order> CreateAsync(OrderType type, int? tableId, int userId, string serverName, CancellationToken cancellationToken = default)
    {
        await EnsureOperationalUserAsync(userId, cancellationToken);
        serverName = serverName.Trim();
        if (string.IsNullOrWhiteSpace(serverName)) throw new InvalidOperationException("Enter the server name before opening a bill.");
        if (type == OrderType.DineIn && tableId is null) throw new InvalidOperationException("Select a table for a dine-in order.");
        var datePart = RestaurantTime.ToLocal(DateTime.UtcNow).ToString("yyyyMMdd");
        var next = await db.Orders.CountAsync(o => o.InvoiceNumber.StartsWith($"POS-{datePart}-"), cancellationToken) + 1;
        var settings = await db.RestaurantSettings.SingleAsync(x => x.Id == 1, cancellationToken);
        var order = new Order { InvoiceNumber = $"POS-{datePart}-{next:0000}", Type = type, DiningTableId = tableId, CreatedByUserId = userId, ServerName = serverName, GstRate = settings.GstRate };
        db.Orders.Add(order);
        AddAudit(userId, AuditAction.Created, "Order", order.InvoiceNumber, $"Created {type} order.");
        await db.SaveChangesAsync(cancellationToken);
        return await LoadAsync(order.Id, cancellationToken);
    }

    public async Task<Order> AddMenuItemAsync(int orderId, int menuItemId, int userId, CancellationToken cancellationToken = default)
        => await AddMenuItemAsync(orderId, menuItemId, userId, cancellationToken, null);
    public async Task<Order> AddMenuItemAsync(int orderId, int menuItemId, PreparationMode preparationMode, int userId, CancellationToken cancellationToken = default)
        => await AddMenuItemAsync(orderId, menuItemId, userId, cancellationToken, preparationMode);

    private async Task<Order> AddMenuItemAsync(int orderId, int menuItemId, int userId, CancellationToken cancellationToken, PreparationMode? requestedMode)
    {
        await EnsureOperationalUserAsync(userId, cancellationToken);
        var order = await LoadOpenAsync(orderId, cancellationToken);
        var item = await db.MenuItems.SingleOrDefaultAsync(x => x.Id == menuItemId && x.IsActive, cancellationToken) ?? throw new InvalidOperationException("This menu item is unavailable.");
        var mode = requestedMode ?? (order.Type == OrderType.Takeaway ? PreparationMode.Packed : PreparationMode.DineIn);
        var line = order.Lines.SingleOrDefault(x => x.MenuItemId == item.Id && x.PreparationMode == mode);
        if (line is null) order.Lines.Add(new OrderLine { MenuItemId = item.Id, ItemName = item.Name, UnitPrice = item.UnitPrice, GstRate = item.GstRate, Quantity = 1, PreparationMode = mode });
        else line.Quantity += 1;
        Recalculate(order);
        AddAudit(userId, AuditAction.Updated, "Order", order.InvoiceNumber, $"Added {item.Name}.");
        await db.SaveChangesAsync(cancellationToken);
        return await LoadAsync(orderId, cancellationToken);
    }

    public async Task<Order> ChangeQuantityAsync(int orderId, int lineId, decimal quantity, int userId, CancellationToken cancellationToken = default)
    {
        await EnsureOperationalUserAsync(userId, cancellationToken);
        var order = await LoadOpenAsync(orderId, cancellationToken);
        var line = order.Lines.SingleOrDefault(x => x.Id == lineId) ?? throw new InvalidOperationException("Order line was not found.");
        if (quantity <= 0)
        {
            order.Lines.Remove(line);
            db.OrderLines.Remove(line);
        }
        else line.Quantity = quantity;
        Recalculate(order);
        AddAudit(userId, AuditAction.Updated, "Order", order.InvoiceNumber, quantity <= 0 ? $"Removed {line.ItemName}." : $"Updated {line.ItemName} quantity.");
        await db.SaveChangesAsync(cancellationToken);
        return await LoadAsync(orderId, cancellationToken);
    }

    public async Task<Order> SetOrderDiscountAsync(int orderId, DiscountType discountType, decimal discountValue, int userId, CancellationToken cancellationToken = default)
    {
        await EnsureOperationalUserAsync(userId, cancellationToken);
        var order = await LoadOpenAsync(orderId, cancellationToken);
        if (discountValue < 0 || (discountType == DiscountType.Percentage && discountValue > 100)) throw new InvalidOperationException("The discount value is invalid.");
        order.DiscountType = discountValue == 0 ? DiscountType.None : discountType;
        order.DiscountValue = discountValue;
        Recalculate(order);
        AddAudit(userId, AuditAction.Updated, "Order", order.InvoiceNumber, $"Applied {discountType} bill discount.");
        await db.SaveChangesAsync(cancellationToken);
        return await LoadAsync(orderId, cancellationToken);
    }

    public async Task<IReadOnlyList<Order>> GetOpenTakeawayOrdersAsync(CancellationToken cancellationToken = default) =>
        await db.Orders.Include(x => x.Lines).Include(x => x.Payments)
            .Where(x => x.Type == OrderType.Takeaway && x.Status == OrderStatus.Held && x.Lines.Any())
            .OrderByDescending(x => x.OpenedUtc).ToListAsync(cancellationToken);

    public async Task<Order> OpenTakeawayAsync(int orderId, int userId, CancellationToken cancellationToken = default)
    {
        await EnsureOperationalUserAsync(userId, cancellationToken);
        var order = await db.Orders.Include(x => x.Lines).Include(x => x.Payments)
            .SingleOrDefaultAsync(x => x.Id == orderId && x.Type == OrderType.Takeaway && (x.Status == OrderStatus.Open || x.Status == OrderStatus.Held), cancellationToken)
            ?? throw new InvalidOperationException("The takeaway order is no longer open.");
        if (order.Status == OrderStatus.Held) { order.Status = OrderStatus.Open; Recalculate(order); AddAudit(userId, AuditAction.Resumed, "Order", order.InvoiceNumber, "Reopened from takeaway queue."); await db.SaveChangesAsync(cancellationToken); }
        return order;
    }

    public async Task<Order> SetLinePreparationModeAsync(int orderId, int lineId, PreparationMode mode, int userId, CancellationToken cancellationToken = default)
    {
        await EnsureOperationalUserAsync(userId, cancellationToken);
        var order = await LoadOpenAsync(orderId, cancellationToken);
        var line = order.Lines.SingleOrDefault(x => x.Id == lineId) ?? throw new InvalidOperationException("Order line was not found.");
        line.PreparationMode = order.Type == OrderType.Takeaway ? PreparationMode.Packed : mode;
        AddAudit(userId, AuditAction.Updated, "Order", order.InvoiceNumber, $"Marked {line.ItemName} as {line.PreparationMode}.");
        await db.SaveChangesAsync(cancellationToken); return order;
    }

    public async Task<Order> HoldTakeawayAsync(int orderId, int userId, CancellationToken cancellationToken = default)
    {
        await EnsureOperationalUserAsync(userId, cancellationToken); var order = await LoadOpenAsync(orderId, cancellationToken);
        if (order.Type != OrderType.Takeaway || order.Lines.Count == 0) throw new InvalidOperationException("Add at least one item before saving a takeaway order.");
        order.Status = OrderStatus.Held; AddAudit(userId, AuditAction.Held, "Order", order.InvoiceNumber, "Saved open takeaway order.");
        await db.SaveChangesAsync(cancellationToken); return order;
    }

    public async Task CancelAsync(int orderId, int userId, CancellationToken cancellationToken = default)
    {
        await EnsureOperationalUserAsync(userId, cancellationToken); var order = await LoadAsync(orderId, cancellationToken);
        if (order.Status is OrderStatus.Paid or OrderStatus.Cancelled) throw new InvalidOperationException("This order is already complete.");
        order.Status = OrderStatus.Cancelled; order.ClosedUtc = DateTime.UtcNow; AddAudit(userId, AuditAction.Cancelled, "Order", order.InvoiceNumber, "Cancelled order."); await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<Order> TakePaymentAsync(int orderId, PaymentMethod method, decimal amount, int userId, CancellationToken cancellationToken = default)
    {
        await EnsureOperationalUserAsync(userId, cancellationToken);
        var order = await LoadOpenAsync(orderId, cancellationToken);
        if (order.Lines.Count == 0) throw new InvalidOperationException("Add at least one item before payment.");
        var due = order.GrandTotal - order.Payments.Sum(p => p.Amount);
        if (amount != due) throw new InvalidOperationException($"Payment must equal the amount due: {due:N2}.");
        order.Payments.Add(new Payment { Method = method, Amount = amount });
        order.Status = OrderStatus.Paid; order.ClosedUtc = DateTime.UtcNow;
        AddAudit(userId, AuditAction.Paid, "Order", order.InvoiceNumber, $"Paid {amount:N2} by {method}.");
        await db.SaveChangesAsync(cancellationToken); return await LoadAsync(orderId, cancellationToken);
    }

    private async Task<Order> LoadOpenAsync(int orderId, CancellationToken ct)
    {
        var order = await LoadAsync(orderId, ct);
        if (order.Status != OrderStatus.Open) throw new InvalidOperationException("Resume the held order before editing or taking payment.");
        return order;
    }
    private async Task<AppUser> EnsureOperationalUserAsync(int userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId && x.IsActive, cancellationToken) ?? throw new InvalidOperationException("The current staff account is unavailable.");
        if (user.Role == UserRole.Admin) throw new InvalidOperationException("Administrator accounts are limited to administration and reports.");
        return user;
    }
    private Task<Order> LoadAsync(int orderId, CancellationToken ct) => db.Orders.Include(x => x.Lines).Include(x => x.Payments).SingleAsync(x => x.Id == orderId, ct);
    private void Recalculate(Order order) { var totals = calculator.Calculate(order); order.DiscountAmount = totals.Discount; order.TaxAmount = totals.Tax; order.GrandTotal = totals.Total; }
    private void AddAudit(int userId, AuditAction action, string type, string id, string detail) => db.AuditEntries.Add(new AuditEntry { UserId = userId, Action = action, EntityType = type, EntityId = id, Detail = detail });
}
