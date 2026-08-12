using Microsoft.EntityFrameworkCore;
using RestaurantPos.Domain;

namespace RestaurantPos.Infrastructure;

public sealed class RestaurantDbContext(DbContextOptions<RestaurantDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<DiningTable> DiningTables => Set<DiningTable>();
    public DbSet<FloorLayout> FloorLayouts => Set<FloorLayout>();
    public DbSet<FloorSection> FloorSections => Set<FloorSection>();
    public DbSet<MenuCategory> MenuCategories => Set<MenuCategory>();
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<RestaurantSettings> RestaurantSettings => Set<RestaurantSettings>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<AppUser>(e => { e.Property(x => x.DisplayName).HasMaxLength(100).IsRequired(); e.Property(x => x.PinHash).HasMaxLength(255).IsRequired(); e.HasIndex(x => x.DisplayName).IsUnique(); });
        b.Entity<FloorLayout>(e => { e.Property(x => x.Name).HasMaxLength(80).IsRequired(); e.HasIndex(x => x.Name).IsUnique(); });
        b.Entity<FloorSection>(e => { e.Property(x => x.Name).HasMaxLength(80).IsRequired(); e.HasIndex(x => new { x.FloorLayoutId, x.Name }).IsUnique(); e.HasOne(x => x.FloorLayout).WithMany(x => x.Sections).HasForeignKey(x => x.FloorLayoutId).OnDelete(DeleteBehavior.Cascade); });
        b.Entity<DiningTable>(e => { e.Property(x => x.Name).HasMaxLength(40).IsRequired(); e.HasIndex(x => x.Name).IsUnique(); e.HasOne(x => x.FloorLayout).WithMany(x => x.Tables).HasForeignKey(x => x.FloorLayoutId).OnDelete(DeleteBehavior.Restrict); e.HasOne(x => x.FloorSection).WithMany(x => x.Tables).HasForeignKey(x => x.FloorSectionId).OnDelete(DeleteBehavior.SetNull); });
        b.Entity<MenuCategory>(e => { e.Property(x => x.Name).HasMaxLength(80).IsRequired(); e.HasIndex(x => x.Name).IsUnique(); });
        b.Entity<MenuItem>(e => { e.Property(x => x.Name).HasMaxLength(120).IsRequired(); e.Property(x => x.UnitPrice).HasPrecision(18, 2); e.Property(x => x.GstRate).HasPrecision(5, 2); e.HasOne(x => x.MenuCategory).WithMany(x => x.Items).HasForeignKey(x => x.MenuCategoryId).OnDelete(DeleteBehavior.Restrict); });
        b.Entity<Order>(e =>
        {
            e.Property(x => x.InvoiceNumber).HasMaxLength(40).IsRequired(); e.Property(x => x.ServerName).HasMaxLength(100).IsRequired(); e.HasIndex(x => x.InvoiceNumber).IsUnique(); e.HasIndex(x => new { x.Status, x.OpenedUtc });
            e.Property(x => x.DiscountValue).HasPrecision(18, 2); e.Property(x => x.DiscountAmount).HasPrecision(18, 2); e.Property(x => x.GstRate).HasPrecision(5, 2); e.Property(x => x.TaxAmount).HasPrecision(18, 2); e.Property(x => x.GrandTotal).HasPrecision(18, 2);
            e.HasOne(x => x.DiningTable).WithMany().HasForeignKey(x => x.DiningTableId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CreatedByUser).WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<OrderLine>(e => { e.Property(x => x.ItemName).HasMaxLength(120).IsRequired(); e.Property(x => x.UnitPrice).HasPrecision(18, 2); e.Property(x => x.GstRate).HasPrecision(5, 2); e.Property(x => x.Quantity).HasPrecision(10, 2); e.Property(x => x.DiscountValue).HasPrecision(18, 2); e.Property(x => x.TaxAmount).HasPrecision(18, 2); e.Property(x => x.LineTotal).HasPrecision(18, 2); e.HasOne(x => x.Order).WithMany(x => x.Lines).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade); });
        b.Entity<Payment>(e => { e.Property(x => x.Amount).HasPrecision(18, 2); e.Property(x => x.Reference).HasMaxLength(100); e.HasOne(x => x.Order).WithMany(x => x.Payments).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade); });
        b.Entity<AuditEntry>(e => { e.Property(x => x.EntityType).HasMaxLength(80).IsRequired(); e.Property(x => x.EntityId).HasMaxLength(80).IsRequired(); e.Property(x => x.Detail).HasMaxLength(500).IsRequired(); e.HasIndex(x => new { x.EntityType, x.EntityId }); e.HasIndex(x => x.OccurredUtc); });
        b.Entity<RestaurantSettings>(e => e.Property(x => x.GstRate).HasPrecision(5, 2));
    }
}
