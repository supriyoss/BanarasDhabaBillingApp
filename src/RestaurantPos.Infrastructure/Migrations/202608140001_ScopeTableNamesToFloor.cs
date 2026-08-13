using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace RestaurantPos.Infrastructure.Migrations;

[DbContext(typeof(RestaurantDbContext))]
[Migration("202608140001_ScopeTableNamesToFloor")]
public partial class ScopeTableNamesToFloor : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_DiningTables_Name", table: "DiningTables");
        migrationBuilder.CreateIndex(name: "IX_DiningTables_FloorLayoutId_Name", table: "DiningTables", columns: new[] { "FloorLayoutId", "Name" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_DiningTables_FloorLayoutId_Name", table: "DiningTables");
        migrationBuilder.CreateIndex(name: "IX_DiningTables_Name", table: "DiningTables", column: "Name", unique: true);
    }
}
