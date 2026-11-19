using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QrDine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OrderCancelledByAndItemTaxZero : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "Orders",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancelledBy",
                table: "Orders",
                type: "text",
                nullable: true);

            // Until now an item's tax of 0 was billed at the restaurant's rate, the same as a blank one. From here on
            // 0 means "no tax", so items saved with 0 become blank and their bills stay exactly as they were.
            // The old build treats blank and 0 alike, so rolling the app back still bills them the same.
            migrationBuilder.Sql("UPDATE \"MenuItems\" SET \"TaxRate\" = NULL WHERE \"TaxRate\" = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The tax fix-up isn't undone: blank was billed the same as 0 before this, so there is nothing to restore.
            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CancelledBy",
                table: "Orders");
        }
    }
}
