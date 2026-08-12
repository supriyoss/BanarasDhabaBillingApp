using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace RestaurantPos.Infrastructure.Migrations;

[DbContext(typeof(RestaurantDbContext))]
[Migration("202608120001_AddServerNameToOrders")]
public partial class AddServerNameToOrders : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<string>(name: "ServerName", table: "Orders", maxLength: 100, nullable: false, defaultValue: "");
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn(name: "ServerName", table: "Orders");
}
