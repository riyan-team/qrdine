using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QrDine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWaiterAutoAssign : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "WaiterAutoAssign",
                table: "Restaurants",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WaiterAutoAssign",
                table: "Restaurants");
        }
    }
}
