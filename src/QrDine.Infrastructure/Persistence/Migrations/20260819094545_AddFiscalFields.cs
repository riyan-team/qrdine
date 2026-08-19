using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QrDine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFiscalFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FiscalNumber",
                table: "Invoices",
                type: "TEXT",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FiscalProvider",
                table: "Invoices",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FiscalQrPayload",
                table: "Invoices",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FiscalStatus",
                table: "Invoices",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FiscalNumber",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "FiscalProvider",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "FiscalQrPayload",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "FiscalStatus",
                table: "Invoices");
        }
    }
}
