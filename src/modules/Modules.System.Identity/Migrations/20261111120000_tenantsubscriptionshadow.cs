using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

#nullable disable

namespace Modules.Identity.Migrations;

/// <summary>
/// FR-17C-1: additive Tenant-scoped order + subscription storage only.
/// No checkout/settlement APIs and NO active seed, TenantModules or grants.
/// </summary>
[DbContext(typeof(IdentityDbContext))]
[Migration("20261111120000_tenantsubscriptionshadow")]
public sealed class TenantSubscriptionShadowMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TenantPlanOrders",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedByIp = table.Column<string?>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                LastModifiedBy = table.Column<Guid?>(type: "uniqueidentifier", nullable: true),
                LastModifiedAt = table.Column<DateTime?>(type: "datetime2", nullable: true),
                LastModifiedByIp = table.Column<string?>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", nullable: false, rowVersion: true),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PlanVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                BillingCycle = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                GrossAmount = table.Column<decimal>(type: "decimal(19,2)", nullable: false),
                DiscountAmount = table.Column<decimal>(type: "decimal(19,2)", nullable: false),
                NetAmount = table.Column<decimal>(type: "decimal(19,2)", nullable: false),
                QuotedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                SettledAtUtc = table.Column<DateTime?>(type: "datetime2", nullable: true),
                SettlementKind = table.Column<string?>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                SettlementReference = table.Column<string?>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                IdempotencyKey = table.Column<string?>(type: "nvarchar(100)", maxLength: 100, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TenantPlanOrders", x => x.Id);
                table.ForeignKey("FK_TenantPlanOrders_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_TenantPlanOrders_CatalogPlanVersions_PlanVersionId", x => x.PlanVersionId, "CatalogPlanVersions", "Id", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateTable(
            name: "TenantSubscriptions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedByIp = table.Column<string?>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                LastModifiedBy = table.Column<Guid?>(type: "uniqueidentifier", nullable: true),
                LastModifiedAt = table.Column<DateTime?>(type: "datetime2", nullable: true),
                LastModifiedByIp = table.Column<string?>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", nullable: false, rowVersion: true),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PlanVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SourceOrderId = table.Column<Guid?>(type: "uniqueidentifier", nullable: true),
                Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                StartsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                EndsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                ActivatedAtUtc = table.Column<DateTime?>(type: "datetime2", nullable: true),
                CancelledAtUtc = table.Column<DateTime?>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TenantSubscriptions", x => x.Id);
                table.ForeignKey("FK_TenantSubscriptions_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_TenantSubscriptions_CatalogPlanVersions_PlanVersionId", x => x.PlanVersionId, "CatalogPlanVersions", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_TenantSubscriptions_TenantPlanOrders_SourceOrderId", x => x.SourceOrderId, "TenantPlanOrders", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_TenantPlanOrders_CreatedAt",
            table: "TenantPlanOrders",
            column: "CreatedAt",
            unique: false,
            descending: new[] { true });
        migrationBuilder.CreateIndex(
            name: "IX_TenantPlanOrders_PlanVersionId",
            table: "TenantPlanOrders",
            column: "PlanVersionId",
            unique: false);
        migrationBuilder.CreateIndex(
            name: "IX_TenantPlanOrders_TenantId_Status_SettledAtUtc",
            table: "TenantPlanOrders",
            columns: new[] { "TenantId", "Status", "SettledAtUtc" },
            unique: false);
        migrationBuilder.CreateIndex(
            name: "IX_TenantPlanOrders_TenantId_IdempotencyKey",
            table: "TenantPlanOrders",
            columns: new[] { "TenantId", "IdempotencyKey" },
            unique: true,
            filter: "[IdempotencyKey] IS NOT NULL");
        migrationBuilder.CreateIndex(
            name: "IX_TenantSubscriptions_CreatedAt",
            table: "TenantSubscriptions",
            column: "CreatedAt",
            unique: false,
            descending: new[] { true });
        migrationBuilder.CreateIndex(
            name: "IX_TenantSubscriptions_PlanVersionId",
            table: "TenantSubscriptions",
            column: "PlanVersionId",
            unique: false);
        migrationBuilder.CreateIndex(
            name: "IX_TenantSubscriptions_TenantId_Status_EndsAtUtc",
            table: "TenantSubscriptions",
            columns: new[] { "TenantId", "Status", "EndsAtUtc" },
            unique: false);
        migrationBuilder.CreateIndex(
            name: "IX_TenantSubscriptions_SourceOrderId",
            table: "TenantSubscriptions",
            column: "SourceOrderId",
            unique: true,
            filter: "[SourceOrderId] IS NOT NULL");
        migrationBuilder.CreateIndex(
            name: "UX_TenantSubscriptions_OneActive",
            table: "TenantSubscriptions",
            column: "TenantId",
            unique: true,
            filter: "[Status] = 'Active'");

        migrationBuilder.AddCheckConstraint(name: "CK_TenantPlanOrders_Status", table: "TenantPlanOrders", sql: "[Status] IN ('Pending','Settled','Cancelled','Refunded')");
        migrationBuilder.AddCheckConstraint(name: "CK_TenantPlanOrders_Amounts", table: "TenantPlanOrders", sql: "[GrossAmount] >= 0 AND [DiscountAmount] >= 0 AND [DiscountAmount] <= [GrossAmount] AND [NetAmount] = [GrossAmount] - [DiscountAmount]");
        migrationBuilder.AddCheckConstraint(name: "CK_TenantPlanOrders_BillingCycle", table: "TenantPlanOrders", sql: "[BillingCycle] IN ('monthly','yearly')");
        migrationBuilder.AddCheckConstraint(name: "CK_TenantPlanOrders_Currency", table: "TenantPlanOrders", sql: "[Currency] = 'IRR'");
        migrationBuilder.AddCheckConstraint(name: "CK_TenantPlanOrders_Settlement", table: "TenantPlanOrders", sql: "([Status] <> 'Settled') OR ([SettledAtUtc] IS NOT NULL AND [SettlementKind] IS NOT NULL AND [SettlementReference] IS NOT NULL AND LEN([SettlementReference]) > 0 AND (([NetAmount] = 0 AND [SettlementKind] = 'ZeroTotalCoupon') OR ([NetAmount] > 0 AND [SettlementKind] IN ('GatewayVerified','ExternalManual'))))");
        migrationBuilder.AddCheckConstraint(name: "CK_TenantSubscriptions_Status", table: "TenantSubscriptions", sql: "[Status] IN ('Pending','Active','Expired','Cancelled')");
        migrationBuilder.AddCheckConstraint(name: "CK_TenantSubscriptions_Period", table: "TenantSubscriptions", sql: "[EndsAtUtc] > [StartsAtUtc]");
        migrationBuilder.AddCheckConstraint(name: "CK_TenantSubscriptions_Activation", table: "TenantSubscriptions", sql: "[Status] <> 'Active' OR ([SourceOrderId] IS NOT NULL AND [ActivatedAtUtc] IS NOT NULL)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "TenantSubscriptions");
        migrationBuilder.DropTable(name: "TenantPlanOrders");
    }
}
