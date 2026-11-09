using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QrDine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OneOpenSeatingOneBill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Sessions_OneOpenPerTable",
                table: "Sessions",
                columns: new[] { "RestaurantId", "DiningTableId" },
                unique: true,
                filter: "\"IsOpen\"");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_OnePerSeating",
                table: "Invoices",
                columns: new[] { "RestaurantId", "TableSessionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sessions_OneOpenPerTable",
                table: "Sessions");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_OnePerSeating",
                table: "Invoices");
        }
    }
}
