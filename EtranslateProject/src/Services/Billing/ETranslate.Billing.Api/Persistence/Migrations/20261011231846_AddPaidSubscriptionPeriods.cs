using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ETranslate.Billing.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaidSubscriptionPeriods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "BillingAnchorUtc",
                schema: "billing",
                table: "subscriptions",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PaidEndsAtUtc",
                schema: "billing",
                table: "subscriptions",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaidMonths",
                schema: "billing",
                table: "subscriptions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PaidPlanName",
                schema: "billing",
                table: "subscriptions",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PaidStartsAtUtc",
                schema: "billing",
                table: "subscriptions",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TenantType",
                schema: "billing",
                table: "subscriptions",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Version",
                schema: "billing",
                table: "subscriptions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "paid_periods",
                schema: "billing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false, collation: "Latin1_General_100_BIN2"),
                    PaymentReference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_100_BIN2"),
                    PlanCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    PlanName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Months = table.Column<int>(type: "int", nullable: false),
                    ConfirmedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AppliedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    StartsAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndsAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_paid_periods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_paid_periods_subscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalSchema: "billing",
                        principalTable: "subscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_paid_periods_Provider_PaymentReference",
                schema: "billing",
                table: "paid_periods",
                columns: new[] { "Provider", "PaymentReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_paid_periods_SubscriptionId",
                schema: "billing",
                table: "paid_periods",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_paid_periods_TenantId",
                schema: "billing",
                table: "paid_periods",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "paid_periods",
                schema: "billing");

            migrationBuilder.DropColumn(
                name: "BillingAnchorUtc",
                schema: "billing",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "PaidEndsAtUtc",
                schema: "billing",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "PaidMonths",
                schema: "billing",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "PaidPlanName",
                schema: "billing",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "PaidStartsAtUtc",
                schema: "billing",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "TenantType",
                schema: "billing",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "Version",
                schema: "billing",
                table: "subscriptions");
        }
    }
}
