using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QrDine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFbrFiscal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "FbrPosId",
                table: "Staff",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FbrToken",
                table: "Staff",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BusinessName",
                table: "Restaurants",
                type: "TEXT",
                maxLength: 150,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "FbrEnabled",
                table: "Restaurants",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "FeatureFbr",
                table: "Restaurants",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Ntn",
                table: "Restaurants",
                type: "TEXT",
                maxLength: 15,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Strn",
                table: "Restaurants",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "FurtherTaxRate",
                table: "MenuItems",
                type: "TEXT",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "PctCode",
                table: "MenuItems",
                type: "TEXT",
                maxLength: 12,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "TaxRate",
                table: "MenuItems",
                type: "TEXT",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerCnic",
                table: "Invoices",
                type: "TEXT",
                maxLength: 15,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BuyerName",
                table: "Invoices",
                type: "TEXT",
                maxLength: 150,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BuyerNtn",
                table: "Invoices",
                type: "TEXT",
                maxLength: 15,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BuyerPhone",
                table: "Invoices",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "FiscalAttempts",
                table: "Invoices",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "FiscalError",
                table: "Invoices",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FiscalLastAttemptAt",
                table: "Invoices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FurtherTaxAmount",
                table: "Invoices",
                type: "TEXT",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "InvoiceType",
                table: "Invoices",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "SyncedByPosId",
                table: "Invoices",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Usin",
                table: "Invoices",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                table: "InvoiceLine",
                type: "TEXT",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "FurtherTaxAmount",
                table: "InvoiceLine",
                type: "TEXT",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "FurtherTaxRate",
                table: "InvoiceLine",
                type: "TEXT",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "ItemCode",
                table: "InvoiceLine",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PctCode",
                table: "InvoiceLine",
                type: "TEXT",
                maxLength: 12,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "SaleValue",
                table: "InvoiceLine",
                type: "TEXT",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxAmount",
                table: "InvoiceLine",
                type: "TEXT",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxRate",
                table: "InvoiceLine",
                type: "TEXT",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalAmount",
                table: "InvoiceLine",
                type: "TEXT",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "FbrEndpointOverride",
                table: "Branches",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FbrMode",
                table: "Branches",
                type: "TEXT",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "FbrPosId",
                table: "Branches",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "FbrSandbox",
                table: "Branches",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "FbrStaticIp",
                table: "Branches",
                type: "TEXT",
                maxLength: 45,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FbrToken",
                table: "Branches",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_RestaurantId_Usin",
                table: "Invoices",
                columns: new[] { "RestaurantId", "Usin" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invoices_RestaurantId_Usin",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "FbrPosId",
                table: "Staff");

            migrationBuilder.DropColumn(
                name: "FbrToken",
                table: "Staff");

            migrationBuilder.DropColumn(
                name: "BusinessName",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "FbrEnabled",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "FeatureFbr",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "Ntn",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "Strn",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "FurtherTaxRate",
                table: "MenuItems");

            migrationBuilder.DropColumn(
                name: "PctCode",
                table: "MenuItems");

            migrationBuilder.DropColumn(
                name: "TaxRate",
                table: "MenuItems");

            migrationBuilder.DropColumn(
                name: "BuyerCnic",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "BuyerName",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "BuyerNtn",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "BuyerPhone",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "FiscalAttempts",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "FiscalError",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "FiscalLastAttemptAt",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "FurtherTaxAmount",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "InvoiceType",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "SyncedByPosId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "Usin",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                table: "InvoiceLine");

            migrationBuilder.DropColumn(
                name: "FurtherTaxAmount",
                table: "InvoiceLine");

            migrationBuilder.DropColumn(
                name: "FurtherTaxRate",
                table: "InvoiceLine");

            migrationBuilder.DropColumn(
                name: "ItemCode",
                table: "InvoiceLine");

            migrationBuilder.DropColumn(
                name: "PctCode",
                table: "InvoiceLine");

            migrationBuilder.DropColumn(
                name: "SaleValue",
                table: "InvoiceLine");

            migrationBuilder.DropColumn(
                name: "TaxAmount",
                table: "InvoiceLine");

            migrationBuilder.DropColumn(
                name: "TaxRate",
                table: "InvoiceLine");

            migrationBuilder.DropColumn(
                name: "TotalAmount",
                table: "InvoiceLine");

            migrationBuilder.DropColumn(
                name: "FbrEndpointOverride",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "FbrMode",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "FbrPosId",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "FbrSandbox",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "FbrStaticIp",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "FbrToken",
                table: "Branches");
        }
    }
}
