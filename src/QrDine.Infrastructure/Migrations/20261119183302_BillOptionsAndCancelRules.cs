using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QrDine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BillOptionsAndCancelRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CardRateForAllItems",
                table: "Restaurants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CashierCancelAnyStage",
                table: "Restaurants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "KeepStockOnCookedCancel",
                table: "Restaurants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "NoCancelOnceCooking",
                table: "Restaurants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RoundTotals",
                table: "Restaurants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ServiceAfterDiscount",
                table: "Restaurants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ServiceChargeTaxable",
                table: "Restaurants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "RoundingAmount",
                table: "Invoices",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ServiceTaxAmount",
                table: "Invoices",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ServiceTaxPercent",
                table: "Invoices",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CardRateForAllItems",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "CashierCancelAnyStage",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "KeepStockOnCookedCancel",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "NoCancelOnceCooking",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "RoundTotals",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "ServiceAfterDiscount",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "ServiceChargeTaxable",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "RoundingAmount",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ServiceTaxAmount",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ServiceTaxPercent",
                table: "Invoices");
        }
    }
}
