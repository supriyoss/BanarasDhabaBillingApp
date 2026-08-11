using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace RestaurantPos.Infrastructure.Migrations;

[DbContext(typeof(RestaurantDbContext))]
[Migration("202608110002_AddOrderLevelDiscount")]
public partial class AddOrderLevelDiscount : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(name: "DiscountType", table: "Orders", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<decimal>(name: "DiscountValue", table: "Orders", type: "TEXT", precision: 18, scale: 2, nullable: false, defaultValue: 0m);
    }
    protected override void Down(MigrationBuilder migrationBuilder) { migrationBuilder.DropColumn(name: "DiscountType", table: "Orders"); migrationBuilder.DropColumn(name: "DiscountValue", table: "Orders"); }
}
