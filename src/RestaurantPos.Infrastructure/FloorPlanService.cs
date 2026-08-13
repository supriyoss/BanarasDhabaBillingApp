using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application;
using RestaurantPos.Domain;

namespace RestaurantPos.Infrastructure;

public sealed class FloorPlanService(RestaurantDbContext db) : IFloorPlanService
{
    public async Task<IReadOnlyList<FloorPlanView>> GetLiveFloorPlansAsync(CancellationToken cancellationToken = default)
    {
        var layouts = await db.FloorLayouts.Include(x => x.Sections).Include(x => x.Tables).Where(x => x.IsActive).OrderBy(x => x.SortOrder).ToListAsync(cancellationToken);
        var activeOrders = await db.Orders.Where(x => x.Type == OrderType.DineIn && (x.Status == OrderStatus.Open || x.Status == OrderStatus.Held)).ToListAsync(cancellationToken);
        return layouts.Select(layout => new FloorPlanView(layout.Id, layout.Name, layout.Tables.Where(x => x.IsActive).Select(table =>
        {
            var order = activeOrders.OrderByDescending(x => x.OpenedUtc).FirstOrDefault(x => x.DiningTableId == table.Id);
            return new FloorTableView(table.Id, table.Name, layout.Sections.FirstOrDefault(x => x.Id == table.FloorSectionId)?.Name, table.Capacity, table.GridX, table.GridY, table.GridWidth, table.GridHeight, table.Shape, order is null ? "Available" : "Occupied", order?.GrandTotal ?? 0, order?.ServerName, order?.OpenedUtc);
        }).ToList())).ToList();
    }

    public async Task<IReadOnlyList<FloorLayout>> GetLayoutsAsync(CancellationToken cancellationToken = default) =>
        await db.FloorLayouts.Include(x => x.Sections).Include(x => x.Tables)
            .Where(x => x.IsActive).OrderBy(x => x.SortOrder).ToListAsync(cancellationToken);

    public async Task<FloorLayout> AddLayoutAsync(string name, int performedByUserId, CancellationToken cancellationToken = default)
    {
        await EnsureEditorAsync(performedByUserId, cancellationToken);
        name = RequiredName(name, "Enter a floor name.");
        if (await db.FloorLayouts.AnyAsync(x => x.Name == name, cancellationToken)) throw new InvalidOperationException("A floor with that name already exists.");
        var layout = new FloorLayout { Name = name, SortOrder = await db.FloorLayouts.CountAsync(cancellationToken) + 1, IsDefault = !await db.FloorLayouts.AnyAsync(cancellationToken) };
        db.FloorLayouts.Add(layout); Audit(performedByUserId, AuditAction.Created, "FloorLayout", name, "Added floor layout.");
        await db.SaveChangesAsync(cancellationToken); return layout;
    }

    public async Task<FloorSection> AddSectionAsync(int layoutId, string name, int performedByUserId, CancellationToken cancellationToken = default)
    {
        await EnsureEditorAsync(performedByUserId, cancellationToken);
        name = RequiredName(name, "Enter a section name.");
        if (!await db.FloorLayouts.AnyAsync(x => x.Id == layoutId && x.IsActive, cancellationToken)) throw new InvalidOperationException("Select an active floor.");
        if (await db.FloorSections.AnyAsync(x => x.FloorLayoutId == layoutId && x.Name == name, cancellationToken)) throw new InvalidOperationException("That section already exists on this floor.");
        var section = new FloorSection { FloorLayoutId = layoutId, Name = name, SortOrder = await db.FloorSections.CountAsync(x => x.FloorLayoutId == layoutId, cancellationToken) + 1 };
        db.FloorSections.Add(section); Audit(performedByUserId, AuditAction.Created, "FloorSection", name, "Added floor section.");
        await db.SaveChangesAsync(cancellationToken); return section;
    }

    public async Task<DiningTable> AddTableAsync(int layoutId, int? sectionId, string name, int capacity, int gridX, int gridY, TableShape shape, int performedByUserId, CancellationToken cancellationToken = default)
    {
        await EnsureEditorAsync(performedByUserId, cancellationToken); ValidateTable(name, capacity, gridX, gridY, 1, 1);
        if (!await db.FloorLayouts.AnyAsync(x => x.Id == layoutId && x.IsActive, cancellationToken)) throw new InvalidOperationException("Select an active floor.");
        await ValidateSectionAsync(layoutId, sectionId, cancellationToken);
        var normalizedName = name.Trim().ToLower();
        if (await db.DiningTables.AnyAsync(x => x.FloorLayoutId == layoutId && x.Name.ToLower() == normalizedName, cancellationToken)) throw new InvalidOperationException("A table with that name already exists on this floor.");
        var table = new DiningTable { FloorLayoutId = layoutId, FloorSectionId = sectionId, Name = name.Trim(), Capacity = capacity, GridX = gridX, GridY = gridY, Shape = shape };
        db.DiningTables.Add(table); Audit(performedByUserId, AuditAction.Created, "DiningTable", table.Name, "Added table to floor plan.");
        await db.SaveChangesAsync(cancellationToken); return table;
    }

    public async Task<DiningTable> UpdateTableAsync(int tableId, string name, int capacity, int gridX, int gridY, int gridWidth, int gridHeight, TableShape shape, int? sectionId, bool isActive, int performedByUserId, CancellationToken cancellationToken = default)
    {
        await EnsureEditorAsync(performedByUserId, cancellationToken); ValidateTable(name, capacity, gridX, gridY, gridWidth, gridHeight);
        var table = await db.DiningTables.SingleOrDefaultAsync(x => x.Id == tableId, cancellationToken) ?? throw new InvalidOperationException("Table was not found.");
        await ValidateSectionAsync(table.FloorLayoutId!.Value, sectionId, cancellationToken);
        var normalizedName = name.Trim().ToLower();
        if (await db.DiningTables.AnyAsync(x => x.Id != tableId && x.FloorLayoutId == table.FloorLayoutId && x.Name.ToLower() == normalizedName, cancellationToken)) throw new InvalidOperationException("A table with that name already exists on this floor.");
        table.Name = name.Trim(); table.Capacity = capacity; table.GridX = gridX; table.GridY = gridY; table.GridWidth = gridWidth; table.GridHeight = gridHeight; table.Shape = shape; table.FloorSectionId = sectionId; table.IsActive = isActive;
        Audit(performedByUserId, AuditAction.Updated, "DiningTable", table.Id.ToString(), "Updated floor-plan table.");
        await db.SaveChangesAsync(cancellationToken); return table;
    }

    private async Task EnsureEditorAsync(int userId, CancellationToken ct)
    {
        var role = await db.Users.Where(x => x.Id == userId && x.IsActive).Select(x => (UserRole?)x.Role).SingleOrDefaultAsync(ct);
        if (role != UserRole.Manager) throw new InvalidOperationException("Only the restaurant manager can edit the floor plan.");
    }
    private async Task ValidateSectionAsync(int layoutId, int? sectionId, CancellationToken ct)
    { if (sectionId is not null && !await db.FloorSections.AnyAsync(x => x.Id == sectionId && x.FloorLayoutId == layoutId && x.IsActive, ct)) throw new InvalidOperationException("Select a section from this floor."); }
    private static string RequiredName(string name, string message) { name = name.Trim(); if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException(message); return name; }
    private static void ValidateTable(string name, int capacity, int x, int y, int width, int height) { RequiredName(name, "Enter a table name."); if (capacity < 1 || x < 0 || y < 0 || width < 1 || height < 1) throw new InvalidOperationException("Enter a valid capacity, position, and size."); }
    private void Audit(int userId, AuditAction action, string type, string id, string detail) => db.AuditEntries.Add(new AuditEntry { UserId = userId, Action = action, EntityType = type, EntityId = id, Detail = detail });
}
