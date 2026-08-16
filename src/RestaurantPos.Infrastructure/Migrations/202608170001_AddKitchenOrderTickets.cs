using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace RestaurantPos.Infrastructure.Migrations;

[DbContext(typeof(RestaurantDbContext))]
[Migration("202608170001_AddKitchenOrderTickets")]
public partial class AddKitchenOrderTickets : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "KitchenOrderTickets",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                OrderId = table.Column<int>(type: "INTEGER", nullable: false),
                TicketNumber = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                SequenceNumber = table.Column<int>(type: "INTEGER", nullable: false),
                IsSupplementary = table.Column<bool>(type: "INTEGER", nullable: false),
                CreatedByUserId = table.Column<int>(type: "INTEGER", nullable: false),
                CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                PrintCount = table.Column<int>(type: "INTEGER", nullable: false),
                LastPrintedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_KitchenOrderTickets", x => x.Id);
                table.ForeignKey("FK_KitchenOrderTickets_Orders_OrderId", x => x.OrderId, "Orders", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_KitchenOrderTickets_Users_CreatedByUserId", x => x.CreatedByUserId, "Users", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "KitchenOrderTicketLines",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                KitchenOrderTicketId = table.Column<int>(type: "INTEGER", nullable: false),
                SourceOrderLineId = table.Column<int>(type: "INTEGER", nullable: false),
                ItemName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                Quantity = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                PreparationMode = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_KitchenOrderTicketLines", x => x.Id);
                table.ForeignKey("FK_KitchenOrderTicketLines_KitchenOrderTickets_KitchenOrderTicketId", x => x.KitchenOrderTicketId, "KitchenOrderTickets", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_KitchenOrderTickets_CreatedByUserId", "KitchenOrderTickets", "CreatedByUserId");
        migrationBuilder.CreateIndex("IX_KitchenOrderTickets_OrderId_SequenceNumber", "KitchenOrderTickets", new[] { "OrderId", "SequenceNumber" }, unique: true);
        migrationBuilder.CreateIndex("IX_KitchenOrderTickets_TicketNumber", "KitchenOrderTickets", "TicketNumber", unique: true);
        migrationBuilder.CreateIndex("IX_KitchenOrderTicketLines_KitchenOrderTicketId", "KitchenOrderTicketLines", "KitchenOrderTicketId");
        migrationBuilder.CreateIndex("IX_KitchenOrderTicketLines_SourceOrderLineId", "KitchenOrderTicketLines", "SourceOrderLineId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "KitchenOrderTicketLines");
        migrationBuilder.DropTable(name: "KitchenOrderTickets");
    }
}
