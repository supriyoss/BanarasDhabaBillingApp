using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace RestaurantPos.Infrastructure.Migrations;

[DbContext(typeof(RestaurantDbContext))]
[Migration("202608130002_AddOrderLinePreparationMode")]
public partial class AddOrderLinePreparationMode : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<int>(name: "PreparationMode", table: "OrderLines", nullable: false, defaultValue: 0);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn(name: "PreparationMode", table: "OrderLines");
}
