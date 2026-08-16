using RestaurantPos.Domain;

namespace RestaurantPos.Application;

public interface IAdministrationService
{
    Task<IReadOnlyList<AppUser>> GetStaffAsync(CancellationToken cancellationToken = default);
    Task<AppUser> AddStaffAsync(UserRole role, string pin, int performedByUserId, CancellationToken cancellationToken = default);
    Task ChangePinAsync(int userId, string currentPin, string newPin, CancellationToken cancellationToken = default);
    Task ResetStaffPinAsync(int userId, string newPin, int performedByUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MenuCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MenuItem>> GetActiveMenuItemsAsync(CancellationToken cancellationToken = default);
    Task<MenuItem> AddMenuItemAsync(int categoryId, string name, decimal price, int performedByUserId, CancellationToken cancellationToken = default);
    Task<MenuItem> UpdateMenuItemAsync(int menuItemId, int categoryId, string name, decimal price, int performedByUserId, CancellationToken cancellationToken = default);
    Task<int> DeactivateMenuItemsAsync(IReadOnlyCollection<int> menuItemIds, int performedByUserId, CancellationToken cancellationToken = default);
    Task<int> DeactivateStaffAccountsAsync(IReadOnlyCollection<int> userIds, int performedByUserId, CancellationToken cancellationToken = default);
    Task<RestaurantSettings> GetSettingsAsync(CancellationToken cancellationToken = default);
    Task UpdateGstRateAsync(decimal gstRate, int performedByUserId, CancellationToken cancellationToken = default);
    Task UpdateReceiptPaperWidthAsync(ReceiptPaperWidth paperWidth, int performedByUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> GetOrderHistoryAsync(DateTime fromDate, CancellationToken cancellationToken = default);
}

public interface IFloorPlanService
{
    Task<IReadOnlyList<FloorLayout>> GetLayoutsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FloorPlanView>> GetLiveFloorPlansAsync(CancellationToken cancellationToken = default);
    Task<FloorLayout> AddLayoutAsync(string name, int performedByUserId, CancellationToken cancellationToken = default);
    Task<FloorSection> AddSectionAsync(int layoutId, string name, int performedByUserId, CancellationToken cancellationToken = default);
    Task<DiningTable> AddTableAsync(int layoutId, int? sectionId, string name, int capacity, int gridX, int gridY, TableShape shape, int performedByUserId, CancellationToken cancellationToken = default);
    Task<DiningTable> UpdateTableAsync(int tableId, string name, int capacity, int gridX, int gridY, int gridWidth, int gridHeight, TableShape shape, int? sectionId, bool isActive, int performedByUserId, CancellationToken cancellationToken = default);
}

public sealed record FloorPlanView(int Id, string Name, IReadOnlyList<FloorTableView> Tables);
public sealed record FloorTableView(int Id, string Name, string? Section, int Capacity, int GridX, int GridY, int GridWidth, int GridHeight, TableShape Shape, string State, decimal RunningTotal, string? ServerName, DateTime? OpenedUtc);
