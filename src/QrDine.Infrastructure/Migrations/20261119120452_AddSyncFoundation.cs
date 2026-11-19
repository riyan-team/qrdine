using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace QrDine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSyncFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastChangedAt",
                table: "Tables",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "Tables",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastChangedAt",
                table: "Suppliers",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "Suppliers",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastChangedAt",
                table: "SupplierEntryLine",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "SupplierEntryLine",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastChangedAt",
                table: "SupplierEntries",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "SupplierEntries",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastChangedAt",
                table: "StockMovements",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "StockMovements",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastChangedAt",
                table: "Stock",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "Stock",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastChangedAt",
                table: "Staff",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "Staff",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastChangedAt",
                table: "Sessions",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "Sessions",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastChangedAt",
                table: "ServiceRequests",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "ServiceRequests",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastChangedAt",
                table: "RolePermissions",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "RolePermissions",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastChangedAt",
                table: "Restaurants",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "Restaurants",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastChangedAt",
                table: "RecipeLine",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "RecipeLine",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastChangedAt",
                table: "Payments",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "Payments",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastChangedAt",
                table: "OrderVoids",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "OrderVoids",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastChangedAt",
                table: "Orders",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "Orders",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastChangedAt",
                table: "OrderItemModifier",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "OrderItemModifier",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastChangedAt",
                table: "OrderItem",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "OrderItem",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastChangedAt",
                table: "ModifierOption",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "ModifierOption",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastChangedAt",
                table: "ModifierGroup",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "ModifierGroup",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastChangedAt",
                table: "MenuItemVariant",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "MenuItemVariant",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastChangedAt",
                table: "MenuItems",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "MenuItems",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastChangedAt",
                table: "LoyaltyTransactions",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "LoyaltyTransactions",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastChangedAt",
                table: "LoyaltyRewards",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "LoyaltyRewards",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastChangedAt",
                table: "LoyaltyRedemptions",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "LoyaltyRedemptions",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastChangedAt",
                table: "LoyaltyPrograms",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "LoyaltyPrograms",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastChangedAt",
                table: "LoyaltyMembers",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "LoyaltyMembers",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastChangedAt",
                table: "Invoices",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "Invoices",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastChangedAt",
                table: "InvoiceLine",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "InvoiceLine",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastChangedAt",
                table: "Feedbacks",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "Feedbacks",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastChangedAt",
                table: "Expenses",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "Expenses",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastChangedAt",
                table: "Employees",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "Employees",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastChangedAt",
                table: "EmployeeEntries",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "EmployeeEntries",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastChangedAt",
                table: "DayClosings",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "DayClosings",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastChangedAt",
                table: "Customers",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "Customers",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastChangedAt",
                table: "CustomerEntries",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "CustomerEntries",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastChangedAt",
                table: "Categories",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "Categories",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastChangedAt",
                table: "Branches",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "Branches",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastChangedAt",
                table: "AttendanceMarks",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SyncId",
                table: "AttendanceMarks",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.CreateTable(
                name: "SyncChanges",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RestaurantId = table.Column<int>(type: "integer", nullable: false),
                    EntityType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    SyncId = table.Column<Guid>(type: "uuid", nullable: false),
                    Op = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncChanges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SyncDevices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RestaurantId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    KeyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncDevices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SyncInbox",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RestaurantId = table.Column<int>(type: "integer", nullable: false),
                    DeviceId = table.Column<int>(type: "integer", nullable: false),
                    ChangeId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityType = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    SyncId = table.Column<Guid>(type: "uuid", nullable: false),
                    Op = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: true),
                    ClientChangedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncInbox", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tables_SyncId",
                table: "Tables",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_SyncId",
                table: "Suppliers",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierEntryLine_SyncId",
                table: "SupplierEntryLine",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierEntries_SyncId",
                table: "SupplierEntries",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_SyncId",
                table: "StockMovements",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stock_SyncId",
                table: "Stock",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Staff_SyncId",
                table: "Staff",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_SyncId",
                table: "Sessions",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRequests_SyncId",
                table: "ServiceRequests",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_SyncId",
                table: "RolePermissions",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Restaurants_SyncId",
                table: "Restaurants",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecipeLine_SyncId",
                table: "RecipeLine",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_SyncId",
                table: "Payments",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderVoids_SyncId",
                table: "OrderVoids",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_SyncId",
                table: "Orders",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderItemModifier_SyncId",
                table: "OrderItemModifier",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderItem_SyncId",
                table: "OrderItem",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ModifierOption_SyncId",
                table: "ModifierOption",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ModifierGroup_SyncId",
                table: "ModifierGroup",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MenuItemVariant_SyncId",
                table: "MenuItemVariant",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MenuItems_SyncId",
                table: "MenuItems",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyTransactions_SyncId",
                table: "LoyaltyTransactions",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyRewards_SyncId",
                table: "LoyaltyRewards",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyRedemptions_SyncId",
                table: "LoyaltyRedemptions",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyPrograms_SyncId",
                table: "LoyaltyPrograms",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyMembers_SyncId",
                table: "LoyaltyMembers",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_SyncId",
                table: "Invoices",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLine_SyncId",
                table: "InvoiceLine",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Feedbacks_SyncId",
                table: "Feedbacks",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_SyncId",
                table: "Expenses",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Employees_SyncId",
                table: "Employees",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeEntries_SyncId",
                table: "EmployeeEntries",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DayClosings_SyncId",
                table: "DayClosings",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_SyncId",
                table: "Customers",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerEntries_SyncId",
                table: "CustomerEntries",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categories_SyncId",
                table: "Categories",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Branches_SyncId",
                table: "Branches",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceMarks_SyncId",
                table: "AttendanceMarks",
                column: "SyncId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SyncChanges_RestaurantId_Id",
                table: "SyncChanges",
                columns: new[] { "RestaurantId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_SyncDevices_KeyHash",
                table: "SyncDevices",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SyncDevices_RestaurantId",
                table: "SyncDevices",
                column: "RestaurantId");

            migrationBuilder.CreateIndex(
                name: "IX_SyncInbox_RestaurantId_ChangeId",
                table: "SyncInbox",
                columns: new[] { "RestaurantId", "ChangeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SyncInbox_RestaurantId_Status_Id",
                table: "SyncInbox",
                columns: new[] { "RestaurantId", "Status", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SyncChanges");

            migrationBuilder.DropTable(
                name: "SyncDevices");

            migrationBuilder.DropTable(
                name: "SyncInbox");

            migrationBuilder.DropIndex(
                name: "IX_Tables_SyncId",
                table: "Tables");

            migrationBuilder.DropIndex(
                name: "IX_Suppliers_SyncId",
                table: "Suppliers");

            migrationBuilder.DropIndex(
                name: "IX_SupplierEntryLine_SyncId",
                table: "SupplierEntryLine");

            migrationBuilder.DropIndex(
                name: "IX_SupplierEntries_SyncId",
                table: "SupplierEntries");

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_SyncId",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "IX_Stock_SyncId",
                table: "Stock");

            migrationBuilder.DropIndex(
                name: "IX_Staff_SyncId",
                table: "Staff");

            migrationBuilder.DropIndex(
                name: "IX_Sessions_SyncId",
                table: "Sessions");

            migrationBuilder.DropIndex(
                name: "IX_ServiceRequests_SyncId",
                table: "ServiceRequests");

            migrationBuilder.DropIndex(
                name: "IX_RolePermissions_SyncId",
                table: "RolePermissions");

            migrationBuilder.DropIndex(
                name: "IX_Restaurants_SyncId",
                table: "Restaurants");

            migrationBuilder.DropIndex(
                name: "IX_RecipeLine_SyncId",
                table: "RecipeLine");

            migrationBuilder.DropIndex(
                name: "IX_Payments_SyncId",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_OrderVoids_SyncId",
                table: "OrderVoids");

            migrationBuilder.DropIndex(
                name: "IX_Orders_SyncId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_OrderItemModifier_SyncId",
                table: "OrderItemModifier");

            migrationBuilder.DropIndex(
                name: "IX_OrderItem_SyncId",
                table: "OrderItem");

            migrationBuilder.DropIndex(
                name: "IX_ModifierOption_SyncId",
                table: "ModifierOption");

            migrationBuilder.DropIndex(
                name: "IX_ModifierGroup_SyncId",
                table: "ModifierGroup");

            migrationBuilder.DropIndex(
                name: "IX_MenuItemVariant_SyncId",
                table: "MenuItemVariant");

            migrationBuilder.DropIndex(
                name: "IX_MenuItems_SyncId",
                table: "MenuItems");

            migrationBuilder.DropIndex(
                name: "IX_LoyaltyTransactions_SyncId",
                table: "LoyaltyTransactions");

            migrationBuilder.DropIndex(
                name: "IX_LoyaltyRewards_SyncId",
                table: "LoyaltyRewards");

            migrationBuilder.DropIndex(
                name: "IX_LoyaltyRedemptions_SyncId",
                table: "LoyaltyRedemptions");

            migrationBuilder.DropIndex(
                name: "IX_LoyaltyPrograms_SyncId",
                table: "LoyaltyPrograms");

            migrationBuilder.DropIndex(
                name: "IX_LoyaltyMembers_SyncId",
                table: "LoyaltyMembers");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_SyncId",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceLine_SyncId",
                table: "InvoiceLine");

            migrationBuilder.DropIndex(
                name: "IX_Feedbacks_SyncId",
                table: "Feedbacks");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_SyncId",
                table: "Expenses");

            migrationBuilder.DropIndex(
                name: "IX_Employees_SyncId",
                table: "Employees");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeEntries_SyncId",
                table: "EmployeeEntries");

            migrationBuilder.DropIndex(
                name: "IX_DayClosings_SyncId",
                table: "DayClosings");

            migrationBuilder.DropIndex(
                name: "IX_Customers_SyncId",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_CustomerEntries_SyncId",
                table: "CustomerEntries");

            migrationBuilder.DropIndex(
                name: "IX_Categories_SyncId",
                table: "Categories");

            migrationBuilder.DropIndex(
                name: "IX_Branches_SyncId",
                table: "Branches");

            migrationBuilder.DropIndex(
                name: "IX_AttendanceMarks_SyncId",
                table: "AttendanceMarks");

            migrationBuilder.DropColumn(
                name: "LastChangedAt",
                table: "Tables");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "Tables");

            migrationBuilder.DropColumn(
                name: "LastChangedAt",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "LastChangedAt",
                table: "SupplierEntryLine");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "SupplierEntryLine");

            migrationBuilder.DropColumn(
                name: "LastChangedAt",
                table: "SupplierEntries");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "SupplierEntries");

            migrationBuilder.DropColumn(
                name: "LastChangedAt",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "LastChangedAt",
                table: "Stock");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "Stock");

            migrationBuilder.DropColumn(
                name: "LastChangedAt",
                table: "Staff");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "Staff");

            migrationBuilder.DropColumn(
                name: "LastChangedAt",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "LastChangedAt",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "ServiceRequests");

            migrationBuilder.DropColumn(
                name: "LastChangedAt",
                table: "RolePermissions");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "RolePermissions");

            migrationBuilder.DropColumn(
                name: "LastChangedAt",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "Restaurants");

            migrationBuilder.DropColumn(
                name: "LastChangedAt",
                table: "RecipeLine");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "RecipeLine");

            migrationBuilder.DropColumn(
                name: "LastChangedAt",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "LastChangedAt",
                table: "OrderVoids");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "OrderVoids");

            migrationBuilder.DropColumn(
                name: "LastChangedAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "LastChangedAt",
                table: "OrderItemModifier");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "OrderItemModifier");

            migrationBuilder.DropColumn(
                name: "LastChangedAt",
                table: "OrderItem");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "OrderItem");

            migrationBuilder.DropColumn(
                name: "LastChangedAt",
                table: "ModifierOption");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "ModifierOption");

            migrationBuilder.DropColumn(
                name: "LastChangedAt",
                table: "ModifierGroup");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "ModifierGroup");

            migrationBuilder.DropColumn(
                name: "LastChangedAt",
                table: "MenuItemVariant");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "MenuItemVariant");

            migrationBuilder.DropColumn(
                name: "LastChangedAt",
                table: "MenuItems");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "MenuItems");

            migrationBuilder.DropColumn(
                name: "LastChangedAt",
                table: "LoyaltyTransactions");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "LoyaltyTransactions");

            migrationBuilder.DropColumn(
                name: "LastChangedAt",
                table: "LoyaltyRewards");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "LoyaltyRewards");

            migrationBuilder.DropColumn(
                name: "LastChangedAt",
                table: "LoyaltyRedemptions");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "LoyaltyRedemptions");

            migrationBuilder.DropColumn(
                name: "LastChangedAt",
                table: "LoyaltyPrograms");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "LoyaltyPrograms");

            migrationBuilder.DropColumn(
                name: "LastChangedAt",
                table: "LoyaltyMembers");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "LoyaltyMembers");

            migrationBuilder.DropColumn(
                name: "LastChangedAt",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "LastChangedAt",
                table: "InvoiceLine");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "InvoiceLine");

            migrationBuilder.DropColumn(
                name: "LastChangedAt",
                table: "Feedbacks");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "Feedbacks");

            migrationBuilder.DropColumn(
                name: "LastChangedAt",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "LastChangedAt",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "Employees");

            migrationBuilder.DropColumn(
                name: "LastChangedAt",
                table: "EmployeeEntries");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "EmployeeEntries");

            migrationBuilder.DropColumn(
                name: "LastChangedAt",
                table: "DayClosings");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "DayClosings");

            migrationBuilder.DropColumn(
                name: "LastChangedAt",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "LastChangedAt",
                table: "CustomerEntries");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "CustomerEntries");

            migrationBuilder.DropColumn(
                name: "LastChangedAt",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "LastChangedAt",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "LastChangedAt",
                table: "AttendanceMarks");

            migrationBuilder.DropColumn(
                name: "SyncId",
                table: "AttendanceMarks");
        }
    }
}
