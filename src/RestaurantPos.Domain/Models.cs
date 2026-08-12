namespace RestaurantPos.Domain;

public enum UserRole { Admin, Manager, Cashier, Server }
public enum OrderType { DineIn, Takeaway }
public enum OrderStatus { Open, Held, Paid, Cancelled }
public enum PaymentMethod { Cash, Card, Upi, Split }
public enum DiscountType { None, Percentage, FixedAmount }
public enum AuditAction { Created, Updated, Held, Resumed, Paid, Reprinted, Cancelled, Login }
public enum TableShape { Square, Rectangle, Round }

public sealed class AppUser
{
    public int Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string PinHash { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class DiningTable
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public bool IsActive { get; set; } = true;
    public int? FloorLayoutId { get; set; }
    public FloorLayout? FloorLayout { get; set; }
    public int? FloorSectionId { get; set; }
    public FloorSection? FloorSection { get; set; }
    public int GridX { get; set; }
    public int GridY { get; set; }
    public int GridWidth { get; set; } = 1;
    public int GridHeight { get; set; } = 1;
    public TableShape Shape { get; set; }
}

public sealed class FloorLayout
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<FloorSection> Sections { get; set; } = new List<FloorSection>();
    public ICollection<DiningTable> Tables { get; set; } = new List<DiningTable>();
}

public sealed class FloorSection
{
    public int Id { get; set; }
    public int FloorLayoutId { get; set; }
    public FloorLayout? FloorLayout { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<DiningTable> Tables { get; set; } = new List<DiningTable>();
}

public sealed class MenuCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public ICollection<MenuItem> Items { get; set; } = new List<MenuItem>();
}

public sealed class MenuItem
{
    public int Id { get; set; }
    public int MenuCategoryId { get; set; }
    public MenuCategory? MenuCategory { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public decimal GstRate { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

public sealed class RestaurantSettings
{
    public int Id { get; set; }
    public decimal GstRate { get; set; } = 5m;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class Order
{
    public int Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public OrderType Type { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Open;
    public int? DiningTableId { get; set; }
    public DiningTable? DiningTable { get; set; }
    public int CreatedByUserId { get; set; }
    public AppUser? CreatedByUser { get; set; }
    public string ServerName { get; set; } = string.Empty;
    public DateTime OpenedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedUtc { get; set; }
    public string? Notes { get; set; }
    public DiscountType DiscountType { get; set; }
    public decimal DiscountValue { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal GstRate { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal GrandTotal { get; set; }
    public bool BillRequested { get; set; }
    public ICollection<OrderLine> Lines { get; set; } = new List<OrderLine>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}

public sealed class OrderLine
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order? Order { get; set; }
    public int MenuItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public decimal GstRate { get; set; }
    public decimal Quantity { get; set; }
    public DiscountType DiscountType { get; set; }
    public decimal DiscountValue { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal LineTotal { get; set; }
}

public sealed class Payment
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order? Order { get; set; }
    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
    public string? Reference { get; set; }
    public DateTime PaidUtc { get; set; } = DateTime.UtcNow;
}

public sealed class AuditEntry
{
    public long Id { get; set; }
    public int? UserId { get; set; }
    public AuditAction Action { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public DateTime OccurredUtc { get; set; } = DateTime.UtcNow;
}
