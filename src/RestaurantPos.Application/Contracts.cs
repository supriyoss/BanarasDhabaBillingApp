using RestaurantPos.Domain;

namespace RestaurantPos.Application;

public interface IReceiptPrinter
{
    Task<bool> PrintAsync(Order order, bool isReprint, CancellationToken cancellationToken = default);
}

public interface IBackupService
{
    Task CreateBackupAsync(CancellationToken cancellationToken = default);
}

public interface IOrderWorkflow
{
    Task<Order> CreateAsync(OrderType type, int? tableId, int userId, string serverName, CancellationToken cancellationToken = default);
    Task<Order> AddMenuItemAsync(int orderId, int menuItemId, int userId, CancellationToken cancellationToken = default);
    Task<Order> ChangeQuantityAsync(int orderId, int lineId, decimal quantity, int userId, CancellationToken cancellationToken = default);
    Task<Order> SetOrderDiscountAsync(int orderId, DiscountType discountType, decimal discountValue, int userId, CancellationToken cancellationToken = default);
    Task<Order> HoldAsync(int orderId, int userId, CancellationToken cancellationToken = default);
    Task<Order> ResumeAsync(int orderId, int userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> GetHeldOrdersAsync(CancellationToken cancellationToken = default);
    Task<Order> TakePaymentAsync(int orderId, PaymentMethod method, decimal amount, int userId, CancellationToken cancellationToken = default);
}

public interface IAuthenticationService
{
    Task<IReadOnlyList<AppUser>> GetActiveUsersAsync(CancellationToken cancellationToken = default);
    Task<AppUser?> AuthenticateAsync(int userId, string pin, CancellationToken cancellationToken = default);
}
