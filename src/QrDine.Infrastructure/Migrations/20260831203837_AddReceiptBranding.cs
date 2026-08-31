using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QrDine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReceiptBranding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LogoUrl",
                table: "Restaurants",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ReceiptFooter",
                table: "Restaurants",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ReceiptHeader",
                table: "Restaurants",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LogoUrl",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "ReceiptFooter",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "ReceiptHeader",
                table: "Restaurants");
        }
    }
}
