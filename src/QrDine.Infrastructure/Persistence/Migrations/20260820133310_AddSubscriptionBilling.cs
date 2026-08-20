using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QrDine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastPaymentAt",
                table: "Restaurants",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SetupFeePaid",
                table: "Restaurants",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "SetupFeePkr",
                table: "Plans",
                type: "TEXT",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "SubscriptionPayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RestaurantId = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Months = table.Column<int>(type: "INTEGER", nullable: false),
                    AmountPkr = table.Column<decimal>(type: "TEXT", precision: 12, scale: 2, nullable: false),
                    PaidAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    NewPaidThrough = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    RecordedBy = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPayments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPayments_RestaurantId",
                table: "SubscriptionPayments",
                column: "RestaurantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubscriptionPayments");

            migrationBuilder.DropColumn(
                name: "LastPaymentAt",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "SetupFeePaid",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "SetupFeePkr",
                table: "Plans");
        }
    }
}
