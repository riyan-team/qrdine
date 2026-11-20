using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace QrDine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FiscalLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FiscalLog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RestaurantId = table.Column<int>(type: "integer", nullable: false),
                    InvoiceId = table.Column<int>(type: "integer", nullable: true),
                    BillNumber = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    BranchId = table.Column<int>(type: "integer", nullable: false),
                    At = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Office = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Environment = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    Trigger = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    By = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    PosId = table.Column<long>(type: "bigint", nullable: true),
                    Outcome = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    FiscalNumber = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Endpoint = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    HttpStatus = table.Column<int>(type: "integer", nullable: true),
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    RequestBody = table.Column<string>(type: "text", nullable: true),
                    ResponseBody = table.Column<string>(type: "text", nullable: true),
                    Attempt = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FiscalLog", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FiscalLog_RestaurantId_At",
                table: "FiscalLog",
                columns: new[] { "RestaurantId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_FiscalLog_RestaurantId_InvoiceId",
                table: "FiscalLog",
                columns: new[] { "RestaurantId", "InvoiceId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FiscalLog");
        }
    }
}
