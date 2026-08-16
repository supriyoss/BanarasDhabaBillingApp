using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace RestaurantPos.Infrastructure.Migrations;

[DbContext(typeof(RestaurantDbContext))]
[Migration("202608160001_AddReceiptPaperWidthToRestaurantSettings")]
public partial class AddReceiptPaperWidthToRestaurantSettings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "ReceiptPaperWidthMm",
            table: "RestaurantSettings",
            type: "INTEGER",
            nullable: false,
            defaultValue: 80);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ReceiptPaperWidthMm", table: "RestaurantSettings");
    }
}
