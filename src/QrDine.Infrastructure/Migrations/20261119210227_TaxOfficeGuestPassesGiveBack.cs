using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace QrDine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TaxOfficeGuestPassesGiveBack : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "GuestPassesFrom",
                table: "Tables",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QrKey",
                table: "Tables",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            // On for every restaurant, as before this setting existed: each bill is sent when it is settled,
            // and bills that couldn't reach the tax office keep being tried.
            migrationBuilder.AddColumn<bool>(
                name: "FiscalAutoRetry",
                table: "Restaurants",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            // On for every restaurant, as before this setting existed: each bill is sent when it is settled,
            // and bills that couldn't reach the tax office keep being tried.
            migrationBuilder.AddColumn<bool>(
                name: "FiscalSendAtSettle",
                table: "Restaurants",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "OneTaxForAllItems",
                table: "Restaurants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "GuestPassId",
                table: "Orders",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhoneCode",
                table: "LoyaltyMembers",
                type: "character varying(6)",
                maxLength: 6,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PhoneCodeTries",
                table: "LoyaltyMembers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "PhoneCodeUntil",
                table: "LoyaltyMembers",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FiscalClaimUntil",
                table: "Invoices",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FiscalEnvironment",
                table: "Invoices",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            // Every branch so far filed with FBR, and in practice (the server simulated filing). A branch that has
            // real FBR numbers keeps going where it went: the sandbox (Test) or production (Live).
            migrationBuilder.AddColumn<string>(
                name: "FiscalAuthority",
                table: "Branches",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "FBR");

            migrationBuilder.AddColumn<string>(
                name: "FiscalEnvironment",
                table: "Branches",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Practice");

            migrationBuilder.Sql(@"
UPDATE ""Branches"" b SET ""FiscalEnvironment"" = CASE WHEN b.""FbrSandbox"" THEN 'Test' ELSE 'Live' END
WHERE EXISTS (SELECT 1 FROM ""Invoices"" i WHERE i.""BranchId"" = b.""Id"" AND i.""FiscalStatus"" = 'Registered'
              AND i.""FiscalProvider"" IN ('FBR', 'Sandbox'));
-- On this computer (the old default) can't work on the online system: branches still in practice go online.
UPDATE ""Branches"" SET ""FbrMode"" = 'Cloud' WHERE ""FiscalEnvironment"" = 'Practice' AND ""FbrMode"" = 'Local';
UPDATE ""Invoices"" SET ""FiscalEnvironment"" = 'Practice' WHERE ""FiscalProvider"" = 'Simulated';
UPDATE ""Invoices"" SET ""FiscalEnvironment"" = 'Test' WHERE ""FiscalProvider"" = 'Sandbox';");

            migrationBuilder.CreateTable(
                name: "PaymentReturns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RestaurantId = table.Column<int>(type: "integer", nullable: false),
                    BranchId = table.Column<int>(type: "integer", nullable: false),
                    TableSessionId = table.Column<int>(type: "integer", nullable: false),
                    TableName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Method = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    GivenBy = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    GivenAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    LastChangedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    SyncId = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentReturns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_RestaurantId_FiscalStatus",
                table: "Invoices",
                columns: new[] { "RestaurantId", "FiscalStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReturns_RestaurantId_GivenAt",
                table: "PaymentReturns",
                columns: new[] { "RestaurantId", "GivenAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReturns_SyncId",
                table: "PaymentReturns",
                column: "SyncId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentReturns");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_RestaurantId_FiscalStatus",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "GuestPassesFrom",
                table: "Tables");

            migrationBuilder.DropColumn(
                name: "QrKey",
                table: "Tables");

            migrationBuilder.DropColumn(
                name: "FiscalAutoRetry",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "FiscalSendAtSettle",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "OneTaxForAllItems",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "GuestPassId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PhoneCode",
                table: "LoyaltyMembers");

            migrationBuilder.DropColumn(
                name: "PhoneCodeTries",
                table: "LoyaltyMembers");

            migrationBuilder.DropColumn(
                name: "PhoneCodeUntil",
                table: "LoyaltyMembers");

            migrationBuilder.DropColumn(
                name: "FiscalClaimUntil",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "FiscalEnvironment",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "FiscalAuthority",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "FiscalEnvironment",
                table: "Branches");
        }
    }
}
