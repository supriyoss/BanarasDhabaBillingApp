using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace RestaurantPos.Infrastructure.Migrations;

[DbContext(typeof(RestaurantDbContext))]
[Migration("202608110003_AddRestaurantSettingsAndOrderGstRate")]
public partial class AddRestaurantSettingsAndOrderGstRate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(name: "GstRate", table: "Orders", type: "TEXT", precision: 5, scale: 2, nullable: false, defaultValue: 0m);
        migrationBuilder.CreateTable(name: "RestaurantSettings", columns: table => new { Id = table.Column<int>(nullable: false), GstRate = table.Column<decimal>(type: "TEXT", precision: 5, scale: 2, nullable: false), UpdatedUtc = table.Column<DateTime>(nullable: false) }, constraints: table => table.PrimaryKey("PK_RestaurantSettings", x => x.Id));
    }
    protected override void Down(MigrationBuilder migrationBuilder) { migrationBuilder.DropTable("RestaurantSettings"); migrationBuilder.DropColumn(name: "GstRate", table: "Orders"); }
}
