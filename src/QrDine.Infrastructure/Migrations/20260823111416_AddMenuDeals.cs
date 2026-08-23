using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QrDine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMenuDeals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DealDays",
                table: "MenuItems",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DealDescription",
                table: "MenuItems",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "DealEndMinutes",
                table: "MenuItems",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DealHideWhenOff",
                table: "MenuItems",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "DealPrice",
                table: "MenuItems",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DealStartMinutes",
                table: "MenuItems",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DealDays",
                table: "MenuItems");

            migrationBuilder.DropColumn(
                name: "DealDescription",
                table: "MenuItems");

            migrationBuilder.DropColumn(
                name: "DealEndMinutes",
                table: "MenuItems");

            migrationBuilder.DropColumn(
                name: "DealHideWhenOff",
                table: "MenuItems");

            migrationBuilder.DropColumn(
                name: "DealPrice",
                table: "MenuItems");

            migrationBuilder.DropColumn(
                name: "DealStartMinutes",
                table: "MenuItems");
        }
    }
}
