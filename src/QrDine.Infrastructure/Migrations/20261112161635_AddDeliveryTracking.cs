using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QrDine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveredAt",
                table: "Sessions",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "DeliveryLat",
                table: "Sessions",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "DeliveryLng",
                table: "Sessions",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DispatchedAt",
                table: "Sessions",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PromisedAt",
                table: "Sessions",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RiderKey",
                table: "Sessions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<double>(
                name: "RiderLat",
                table: "Sessions",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "RiderLng",
                table: "Sessions",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RiderPhone",
                table: "Sessions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "RiderSeenAt",
                table: "Sessions",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DeliveryRadiusKm",
                table: "Restaurants",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                table: "Restaurants",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                table: "Restaurants",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeliveredAt",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "DeliveryLat",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "DeliveryLng",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "DispatchedAt",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "PromisedAt",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "RiderKey",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "RiderLat",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "RiderLng",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "RiderPhone",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "RiderSeenAt",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "DeliveryRadiusKm",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "Restaurants");
        }
    }
}
