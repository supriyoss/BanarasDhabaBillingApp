using RestaurantPos.Domain;

namespace RestaurantPos.Application;

public interface IAdministrationService
{
    Task<IReadOnlyList<AppUser>> GetStaffAsync(CancellationToken cancellationToken = default);
    Task<AppUser> AddStaffAsync(string displayName, string pin, UserRole role, int performedByUserId, CancellationToken cancellationToken = default);
    Task ChangePinAsync(int userId, string currentPin, string newPin, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MenuCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default);
    Task<MenuItem> AddMenuItemAsync(int categoryId, string name, decimal price, int performedByUserId, CancellationToken cancellationToken = default);
    Task<RestaurantSettings> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task UpdateGstRateAsync(decimal gstRate, int performedByUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> GetOrderHistoryAsync(DateTime fromDate, CancellationToken cancellationToken = default);
}
