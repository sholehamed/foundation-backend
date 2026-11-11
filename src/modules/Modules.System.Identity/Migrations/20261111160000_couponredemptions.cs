using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

#nullable disable

namespace Modules.Identity.Migrations;

/// <summary>FR-17E-1 additive coupon/settled-redemption ledger, no seed or payment grant.</summary>
[DbContext(typeof(IdentityDbContext))]
[Migration("20261111160000_couponredemptions")]
public sealed class CatalogCouponRedemptionsMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "CatalogDiscountCoupons",
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
                CodeHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                Title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                Value = table.Column<decimal>(type: "decimal(19,2)", nullable: false),
                PlanVersionId = table.Column<Guid?>(type: "uniqueidentifier", nullable: true),
                Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                StartsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                EndsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                MaxUses = table.Column<int>(type: "int", nullable: false),
                MaxUsesPerTenant = table.Column<int>(type: "int", nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CatalogDiscountCoupons", x => x.Id);
                table.ForeignKey("FK_CatalogDiscountCoupons_CatalogPlanVersions_PlanVersionId",
                    x => x.PlanVersionId, "CatalogPlanVersions", "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.AddColumn<Guid?>(
            name: "CouponId", table: "TenantPlanOrders",
            type: "uniqueidentifier", nullable: true);
        migrationBuilder.CreateIndex(name: "IX_TenantPlanOrders_CouponId",
            table: "TenantPlanOrders", column: "CouponId");
        migrationBuilder.AddForeignKey(name: "FK_TenantPlanOrders_CatalogDiscountCoupons_CouponId",
            table: "TenantPlanOrders", column: "CouponId",
            principalTable: "CatalogDiscountCoupons", principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.CreateTable(
            name: "CatalogCouponRedemptions",
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
                CouponId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DiscountAmount = table.Column<decimal>(type: "decimal(19,2)", nullable: false),
                RedeemedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CatalogCouponRedemptions", x => x.Id);
                table.ForeignKey("FK_CatalogCouponRedemptions_CatalogDiscountCoupons_CouponId",
                    x => x.CouponId, "CatalogDiscountCoupons", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_CatalogCouponRedemptions_TenantPlanOrders_OrderId",
                    x => x.OrderId, "TenantPlanOrders", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_CatalogCouponRedemptions_Tenants_TenantId",
                    x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex(name: "IX_CatalogDiscountCoupons_CodeHash",
            table: "CatalogDiscountCoupons", column: "CodeHash", unique: true);
        migrationBuilder.CreateIndex(name: "IX_CatalogDiscountCoupons_CreatedAt",
            table: "CatalogDiscountCoupons", column: "CreatedAt", descending: new[] { true });
        migrationBuilder.CreateIndex(name: "IX_CatalogDiscountCoupons_PlanVersionId",
            table: "CatalogDiscountCoupons", column: "PlanVersionId");
        migrationBuilder.CreateIndex(name: "IX_CatalogDiscountCoupons_IsActive_StartsAtUtc_EndsAtUtc",
            table: "CatalogDiscountCoupons", columns: new[] { "IsActive", "StartsAtUtc", "EndsAtUtc" });
        migrationBuilder.CreateIndex(name: "IX_CatalogCouponRedemptions_CreatedAt",
            table: "CatalogCouponRedemptions", column: "CreatedAt", descending: new[] { true });
        migrationBuilder.CreateIndex(name: "IX_CatalogCouponRedemptions_OrderId",
            table: "CatalogCouponRedemptions", column: "OrderId", unique: true);
        migrationBuilder.CreateIndex(name: "IX_CatalogCouponRedemptions_TenantId",
            table: "CatalogCouponRedemptions", column: "TenantId");
        migrationBuilder.CreateIndex(name: "IX_CatalogCouponRedemptions_CouponId_TenantId_RedeemedAtUtc",
            table: "CatalogCouponRedemptions",
            columns: new[] { "CouponId", "TenantId", "RedeemedAtUtc" });
        migrationBuilder.AddCheckConstraint("CK_CatalogDiscountCoupons_Kind",
            "CatalogDiscountCoupons", "[Kind] IN ('Percentage','FixedIRR')");
        migrationBuilder.AddCheckConstraint("CK_CatalogDiscountCoupons_Value",
            "CatalogDiscountCoupons", "[Value] > 0 AND ([Kind] <> 'Percentage' OR [Value] <= 100)");
        migrationBuilder.AddCheckConstraint("CK_CatalogDiscountCoupons_Dates",
            "CatalogDiscountCoupons", "[EndsAtUtc] > [StartsAtUtc]");
        migrationBuilder.AddCheckConstraint("CK_CatalogDiscountCoupons_Uses",
            "CatalogDiscountCoupons", "[MaxUses] > 0 AND [MaxUsesPerTenant] > 0 AND [MaxUsesPerTenant] <= [MaxUses]");
        migrationBuilder.AddCheckConstraint("CK_CatalogDiscountCoupons_Currency",
            "CatalogDiscountCoupons", "[Currency] = 'IRR'");
        migrationBuilder.AddCheckConstraint("CK_CatalogCouponRedemptions_Discount",
            "CatalogCouponRedemptions", "[DiscountAmount] > 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "CatalogCouponRedemptions");
        migrationBuilder.DropForeignKey(
            name: "FK_TenantPlanOrders_CatalogDiscountCoupons_CouponId",
            table: "TenantPlanOrders");
        migrationBuilder.DropIndex(name: "IX_TenantPlanOrders_CouponId", table: "TenantPlanOrders");
        migrationBuilder.DropColumn(name: "CouponId", table: "TenantPlanOrders");
        migrationBuilder.DropTable(name: "CatalogDiscountCoupons");
    }
}
