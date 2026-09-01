using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QrDine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DiscountEnabled",
                table: "Restaurants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "DiscountIsPercent",
                table: "Restaurants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountValue",
                table: "Restaurants",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "ReceiptEnabled",
                table: "Restaurants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "StockTrackingEnabled",
                table: "Restaurants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "TaxEnabled",
                table: "Restaurants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TaxName",
                table: "Restaurants",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "AmountTendered",
                table: "Invoices",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ChangeGiven",
                table: "Invoices",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "TaxLabel",
                table: "Invoices",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiscountEnabled",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "DiscountIsPercent",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "DiscountValue",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "ReceiptEnabled",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "StockTrackingEnabled",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "TaxEnabled",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "TaxName",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "AmountTendered",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ChangeGiven",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "TaxLabel",
                table: "Invoices");
        }
    }
}
