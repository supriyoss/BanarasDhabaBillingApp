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
    Task<Order?> FindActiveTableOrderAsync(int tableId, int userId, CancellationToken cancellationToken = default);
    Task<Order> StartWithMenuItemAsync(OrderType type, int? tableId, int menuItemId, PreparationMode preparationMode, int userId, string serverName, CancellationToken cancellationToken = default);
    Task<Order> AddMenuItemAsync(int orderId, int menuItemId, int userId, CancellationToken cancellationToken = default);
    Task<Order> AddMenuItemAsync(int orderId, int menuItemId, PreparationMode preparationMode, int userId, CancellationToken cancellationToken = default);
    Task<Order> ChangeQuantityAsync(int orderId, int lineId, decimal quantity, int userId, CancellationToken cancellationToken = default);
    Task<Order> SetOrderDiscountAsync(int orderId, DiscountType discountType, decimal discountValue, int userId, CancellationToken cancellationToken = default);
    Task<Order> SetServerNameAsync(int orderId, string serverName, int userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> GetOpenTakeawayOrdersAsync(CancellationToken cancellationToken = default);
    Task<Order> OpenTakeawayAsync(int orderId, int userId, CancellationToken cancellationToken = default);
    Task<Order> SetLinePreparationModeAsync(int orderId, int lineId, PreparationMode mode, int userId, CancellationToken cancellationToken = default);
    Task<Order> HoldTakeawayAsync(int orderId, int userId, CancellationToken cancellationToken = default);
    Task CancelAsync(int orderId, int userId, CancellationToken cancellationToken = default);
    Task<Order> TakePaymentAsync(int orderId, PaymentMethod method, decimal amount, int userId, CancellationToken cancellationToken = default);
}

public interface IAuthenticationService
{
    Task<IReadOnlyList<AppUser>> GetActiveUsersAsync(CancellationToken cancellationToken = default);
    Task<AppUser?> AuthenticateAsync(int userId, string pin, CancellationToken cancellationToken = default);
}
