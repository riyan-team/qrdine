using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QrDine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderPerfIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Sessions_RestaurantId_DiningTableId_IsOpen",
                table: "Sessions",
                columns: new[] { "RestaurantId", "DiningTableId", "IsOpen" });

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_RestaurantId_TableCode_IsOpen",
                table: "Sessions",
                columns: new[] { "RestaurantId", "TableCode", "IsOpen" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_RestaurantId_TableSessionId",
                table: "Payments",
                columns: new[] { "RestaurantId", "TableSessionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_RestaurantId_CreatedAt",
                table: "Orders",
                columns: new[] { "RestaurantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_RestaurantId_Status",
                table: "Orders",
                columns: new[] { "RestaurantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_RestaurantId_TableCode",
                table: "Orders",
                columns: new[] { "RestaurantId", "TableCode" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_RestaurantId_TableSessionId",
                table: "Orders",
                columns: new[] { "RestaurantId", "TableSessionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sessions_RestaurantId_DiningTableId_IsOpen",
                table: "Sessions");

            migrationBuilder.DropIndex(
                name: "IX_Sessions_RestaurantId_TableCode_IsOpen",
                table: "Sessions");

            migrationBuilder.DropIndex(
                name: "IX_Payments_RestaurantId_TableSessionId",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Orders_RestaurantId_CreatedAt",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_RestaurantId_Status",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_RestaurantId_TableCode",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Orders_RestaurantId_TableSessionId",
                table: "Orders");
        }
    }
}
