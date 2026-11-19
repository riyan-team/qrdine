using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QrDine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HybridSyncApply : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AppliedAt",
                table: "SyncInbox",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Attempts",
                table: "SyncInbox",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "SyncInbox",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CounterSince",
                table: "SyncDevices",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OriginDeviceId",
                table: "SyncChanges",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AppliedAt",
                table: "SyncInbox");

            migrationBuilder.DropColumn(
                name: "Attempts",
                table: "SyncInbox");

            migrationBuilder.DropColumn(
                name: "Note",
                table: "SyncInbox");

            migrationBuilder.DropColumn(
                name: "CounterSince",
                table: "SyncDevices");

            migrationBuilder.DropColumn(
                name: "OriginDeviceId",
                table: "SyncChanges");
        }
    }
}
