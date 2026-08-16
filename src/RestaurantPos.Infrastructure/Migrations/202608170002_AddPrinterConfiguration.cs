using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace RestaurantPos.Infrastructure.Migrations;

[DbContext(typeof(RestaurantDbContext))]
[Migration("202608170002_AddPrinterConfiguration")]
public partial class AddPrinterConfiguration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "ReceiptPrinterName", table: "RestaurantSettings", type: "TEXT", maxLength: 260, nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>(name: "KitchenPrinterName", table: "RestaurantSettings", type: "TEXT", maxLength: 260, nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<bool>(name: "UseSamePrinterForKitchen", table: "RestaurantSettings", type: "INTEGER", nullable: false, defaultValue: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ReceiptPrinterName", table: "RestaurantSettings");
        migrationBuilder.DropColumn(name: "KitchenPrinterName", table: "RestaurantSettings");
        migrationBuilder.DropColumn(name: "UseSamePrinterForKitchen", table: "RestaurantSettings");
    }
}
