using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace RestaurantPos.Infrastructure.Migrations;

[DbContext(typeof(RestaurantDbContext))]
[Migration("202608130001_AddConfigurableFloorPlans")]
public partial class AddConfigurableFloorPlans : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "FloorLayouts", columns: table => new
        {
            Id = table.Column<int>(nullable: false).Annotation("Sqlite:Autoincrement", true),
            Name = table.Column<string>(maxLength: 80, nullable: false), SortOrder = table.Column<int>(nullable: false),
            IsDefault = table.Column<bool>(nullable: false), IsActive = table.Column<bool>(nullable: false)
        }, constraints: table => table.PrimaryKey("PK_FloorLayouts", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_FloorLayouts_Name", table: "FloorLayouts", column: "Name", unique: true);

        migrationBuilder.CreateTable(name: "FloorSections", columns: table => new
        {
            Id = table.Column<int>(nullable: false).Annotation("Sqlite:Autoincrement", true), FloorLayoutId = table.Column<int>(nullable: false),
            Name = table.Column<string>(maxLength: 80, nullable: false), SortOrder = table.Column<int>(nullable: false), IsActive = table.Column<bool>(nullable: false)
        }, constraints: table => { table.PrimaryKey("PK_FloorSections", x => x.Id); table.ForeignKey("FK_FloorSections_FloorLayouts_FloorLayoutId", x => x.FloorLayoutId, "FloorLayouts", "Id", onDelete: ReferentialAction.Cascade); });
        migrationBuilder.CreateIndex(name: "IX_FloorSections_FloorLayoutId_Name", table: "FloorSections", columns: new[] { "FloorLayoutId", "Name" }, unique: true);

        migrationBuilder.AddColumn<int>(name: "FloorLayoutId", table: "DiningTables", nullable: true);
        migrationBuilder.AddColumn<int>(name: "FloorSectionId", table: "DiningTables", nullable: true);
        migrationBuilder.AddColumn<int>(name: "GridX", table: "DiningTables", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "GridY", table: "DiningTables", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "GridWidth", table: "DiningTables", nullable: false, defaultValue: 1);
        migrationBuilder.AddColumn<int>(name: "GridHeight", table: "DiningTables", nullable: false, defaultValue: 1);
        migrationBuilder.AddColumn<int>(name: "Shape", table: "DiningTables", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<bool>(name: "BillRequested", table: "Orders", nullable: false, defaultValue: false);
        migrationBuilder.CreateIndex(name: "IX_DiningTables_FloorLayoutId", table: "DiningTables", column: "FloorLayoutId");
        migrationBuilder.CreateIndex(name: "IX_DiningTables_FloorSectionId", table: "DiningTables", column: "FloorSectionId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_DiningTables_FloorLayoutId", "DiningTables"); migrationBuilder.DropIndex("IX_DiningTables_FloorSectionId", "DiningTables");
        foreach (var column in new[] { "FloorLayoutId", "FloorSectionId", "GridX", "GridY", "GridWidth", "GridHeight", "Shape" }) migrationBuilder.DropColumn(column, "DiningTables");
        migrationBuilder.DropColumn("BillRequested", "Orders");
        migrationBuilder.DropTable("FloorSections"); migrationBuilder.DropTable("FloorLayouts");
    }
}
