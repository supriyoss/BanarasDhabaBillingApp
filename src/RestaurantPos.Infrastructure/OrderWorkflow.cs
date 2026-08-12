using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application;
using RestaurantPos.Domain;

namespace RestaurantPos.Infrastructure;

public sealed class OrderWorkflow(RestaurantDbContext db, IOrderCalculator calculator) : IOrderWorkflow
{
    public async Task<Order> CreateAsync(OrderType type, int? tableId, int userId, string serverName, CancellationToken cancellationToken = default)
    {
        await EnsureOperationalUserAsync(userId, cancellationToken);
        serverName = serverName.Trim();
        if (string.IsNullOrWhiteSpace(serverName)) throw new InvalidOperationException("Enter the server name before opening a bill.");
        if (type == OrderType.DineIn && tableId is null) throw new InvalidOperationException("Select a table for a dine-in order.");
        var datePart = DateTime.Now.ToString("yyyyMMdd");
        var next = await db.Orders.CountAsync(o => o.InvoiceNumber.StartsWith($"POS-{datePart}-"), cancellationToken) + 1;
        var settings = await db.RestaurantSettings.SingleAsync(x => x.Id == 1, cancellationToken);
        var order = new Order { InvoiceNumber = $"POS-{datePart}-{next:0000}", Type = type, DiningTableId = tableId, CreatedByUserId = userId, ServerName = serverName, GstRate = settings.GstRate };
        db.Orders.Add(order);
        AddAudit(userId, AuditAction.Created, "Order", order.InvoiceNumber, $"Created {type} order.");
        await db.SaveChangesAsync(cancellationToken);
        return await LoadAsync(order.Id, cancellationToken);
    }

    public async Task<Order> AddMenuItemAsync(int orderId, int menuItemId, int userId, CancellationToken cancellationToken = default)
    {
        await EnsureOperationalUserAsync(userId, cancellationToken);
        var order = await LoadOpenAsync(orderId, cancellationToken);
        var item = await db.MenuItems.SingleOrDefaultAsync(x => x.Id == menuItemId && x.IsActive, cancellationToken) ?? throw new InvalidOperationException("This menu item is unavailable.");
        var line = order.Lines.SingleOrDefault(x => x.MenuItemId == item.Id);
        if (line is null) order.Lines.Add(new OrderLine { MenuItemId = item.Id, ItemName = item.Name, UnitPrice = item.UnitPrice, GstRate = item.GstRate, Quantity = 1 });
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

    public async Task<Order> HoldAsync(int orderId, int userId, CancellationToken cancellationToken = default)
    {
        await EnsureOperationalUserAsync(userId, cancellationToken);
        var order = await LoadOpenAsync(orderId, cancellationToken);
        order.Status = OrderStatus.Held;
        AddAudit(userId, AuditAction.Held, "Order", order.InvoiceNumber, "Order held.");
        await db.SaveChangesAsync(cancellationToken); return await LoadAsync(orderId, cancellationToken);
    }

    public async Task<Order> ResumeAsync(int orderId, int userId, CancellationToken cancellationToken = default)
    {
        await EnsureOperationalUserAsync(userId, cancellationToken);
        var order = await LoadAsync(orderId, cancellationToken);
        if (order.Status != OrderStatus.Held) throw new InvalidOperationException("Only held orders can be resumed.");
        order.Status = OrderStatus.Open;
        Recalculate(order);
        AddAudit(userId, AuditAction.Resumed, "Order", order.InvoiceNumber, "Order resumed.");
        await db.SaveChangesAsync(cancellationToken); return await LoadAsync(orderId, cancellationToken);
    }

    public async Task<IReadOnlyList<Order>> GetHeldOrdersAsync(CancellationToken cancellationToken = default) =>
        await db.Orders.Include(x => x.DiningTable).Include(x => x.Lines).Where(x => x.Status == OrderStatus.Held).OrderBy(x => x.OpenedUtc).ToListAsync(cancellationToken);

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
