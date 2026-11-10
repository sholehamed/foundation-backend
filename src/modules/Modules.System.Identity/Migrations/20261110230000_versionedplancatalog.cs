using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

#nullable disable

namespace Modules.Identity.Migrations;

/// <summary>
/// FR-17B-1: catalog-only, no grant or subscription seed. Seven empty tables.
/// No existing tenant/module/grant/role rows are modified.
/// </summary>
[DbContext(typeof(IdentityDbContext))]
[Migration("20261110230000_versionedplancatalog")]
public sealed class VersionedPlanCatalogMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "CatalogPlans",
            columns: table => new
            {
                Id = table.Column<Guid>(
                    type: "uniqueidentifier",
                    nullable: false),
                Code = table.Column<string>(
                    type: "nvarchar(80)", maxLength: 80,
                    nullable: false),
                Name = table.Column<string>(
                    type: "nvarchar(200)", maxLength: 200,
                    nullable: false),
                Description = table.Column<string>(
                    type: "nvarchar(1000)", maxLength: 1000,
                    nullable: true),
                ParentPlanId = table.Column<Guid>(
                    type: "uniqueidentifier",
                    nullable: true),
                IsActive = table.Column<bool>(
                    type: "bit",
                    nullable: false),
                SortOrder = table.Column<int>(
                    type: "int",
                    nullable: false),
                CreatedBy = table.Column<Guid>(
                    type: "uniqueidentifier",
                    nullable: false),
                CreatedAt = table.Column<DateTime>(
                    type: "datetime2",
                    nullable: false),
                CreatedByIp = table.Column<string>(
                    type: "nvarchar(45)", maxLength: 45,
                    nullable: true),
                LastModifiedBy = table.Column<Guid>(
                    type: "uniqueidentifier",
                    nullable: true),
                LastModifiedAt = table.Column<DateTime>(
                    type: "datetime2",
                    nullable: true),
                LastModifiedByIp = table.Column<string>(
                    type: "nvarchar(45)", maxLength: 45,
                    nullable: true),
                RowVersion = table.Column<byte[]>(
                    type: "rowversion",
                    rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CatalogPlans", x => x.Id);
                table.ForeignKey(
                    name: "FK_CatalogPlans_CatalogPlans_ParentPlanId",
                    column: x => x.ParentPlanId,
                    principalTable: "CatalogPlans",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "CatalogPlanVersions",
            columns: table => new
            {
                Id = table.Column<Guid>(
                    type: "uniqueidentifier",
                    nullable: false),
                PlanId = table.Column<Guid>(
                    type: "uniqueidentifier",
                    nullable: false),
                ParentPlanVersionId = table.Column<Guid>(
                    type: "uniqueidentifier",
                    nullable: true),
                VersionNumber = table.Column<int>(
                    type: "int",
                    nullable: false),
                Status = table.Column<string>(
                    type: "nvarchar(16)", maxLength: 16,
                    nullable: false),
                PublishedAtUtc = table.Column<DateTime>(
                    type: "datetime2",
                    nullable: true),
                ContentFingerprint = table.Column<string>(
                    type: "nvarchar(64)", maxLength: 64,
                    nullable: true),
                DisplayTitle = table.Column<string>(
                    type: "nvarchar(200)", maxLength: 200,
                    nullable: false),
                DisplaySubtitle = table.Column<string>(
                    type: "nvarchar(1000)", maxLength: 1000,
                    nullable: true),
                BadgeText = table.Column<string>(
                    type: "nvarchar(80)", maxLength: 80,
                    nullable: true),
                IsRecommended = table.Column<bool>(
                    type: "bit",
                    nullable: false),
                DisplayOrder = table.Column<int>(
                    type: "int",
                    nullable: false),
                CreatedBy = table.Column<Guid>(
                    type: "uniqueidentifier",
                    nullable: false),
                CreatedAt = table.Column<DateTime>(
                    type: "datetime2",
                    nullable: false),
                CreatedByIp = table.Column<string>(
                    type: "nvarchar(45)", maxLength: 45,
                    nullable: true),
                LastModifiedBy = table.Column<Guid>(
                    type: "uniqueidentifier",
                    nullable: true),
                LastModifiedAt = table.Column<DateTime>(
                    type: "datetime2",
                    nullable: true),
                LastModifiedByIp = table.Column<string>(
                    type: "nvarchar(45)", maxLength: 45,
                    nullable: true),
                RowVersion = table.Column<byte[]>(
                    type: "rowversion",
                    rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CatalogPlanVersions", x => x.Id);
                table.ForeignKey(
                    name: "FK_CatalogPlanVersions_CatalogPlans_PlanId",
                    column: x => x.PlanId,
                    principalTable: "CatalogPlans",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CatalogPlanVersions_CatalogPlanVersions_ParentPlanVersionId",
                    column: x => x.ParentPlanVersionId,
                    principalTable: "CatalogPlanVersions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "CatalogPlanVersionModules",
            columns: table => new
            {
                Id = table.Column<Guid>(
                    type: "uniqueidentifier",
                    nullable: false),
                PlanVersionId = table.Column<Guid>(
                    type: "uniqueidentifier",
                    nullable: false),
                ModuleId = table.Column<Guid>(
                    type: "uniqueidentifier",
                    nullable: false),
                CreatedBy = table.Column<Guid>(
                    type: "uniqueidentifier",
                    nullable: false),
                CreatedAt = table.Column<DateTime>(
                    type: "datetime2",
                    nullable: false),
                CreatedByIp = table.Column<string>(
                    type: "nvarchar(45)", maxLength: 45,
                    nullable: true),
                LastModifiedBy = table.Column<Guid>(
                    type: "uniqueidentifier",
                    nullable: true),
                LastModifiedAt = table.Column<DateTime>(
                    type: "datetime2",
                    nullable: true),
                LastModifiedByIp = table.Column<string>(
                    type: "nvarchar(45)", maxLength: 45,
                    nullable: true),
                RowVersion = table.Column<byte[]>(
                    type: "rowversion",
                    rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CatalogPlanVersionModules", x => x.Id);
                table.ForeignKey(
                    name: "FK_CatalogPlanVersionModules_CatalogPlanVersions_PlanVersionId",
                    column: x => x.PlanVersionId,
                    principalTable: "CatalogPlanVersions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CatalogPlanVersionModules_Modules_ModuleId",
                    column: x => x.ModuleId,
                    principalTable: "Modules",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "CatalogCapabilityDefinitions",
            columns: table => new
            {
                Id = table.Column<Guid>(
                    type: "uniqueidentifier",
                    nullable: false),
                Code = table.Column<string>(
                    type: "nvarchar(150)", maxLength: 150,
                    nullable: false),
                ModuleId = table.Column<Guid>(
                    type: "uniqueidentifier",
                    nullable: false),
                ResourceId = table.Column<Guid>(
                    type: "uniqueidentifier",
                    nullable: true),
                Title = table.Column<string>(
                    type: "nvarchar(200)", maxLength: 200,
                    nullable: false),
                Kind = table.Column<string>(
                    type: "nvarchar(32)", maxLength: 32,
                    nullable: false),
                ValueKind = table.Column<string>(
                    type: "nvarchar(32)", maxLength: 32,
                    nullable: false),
                MergePolicy = table.Column<string>(
                    type: "nvarchar(32)", maxLength: 32,
                    nullable: false),
                Unit = table.Column<string>(
                    type: "nvarchar(40)", maxLength: 40,
                    nullable: true),
                IsActive = table.Column<bool>(
                    type: "bit",
                    nullable: false),
                CreatedBy = table.Column<Guid>(
                    type: "uniqueidentifier",
                    nullable: false),
                CreatedAt = table.Column<DateTime>(
                    type: "datetime2",
                    nullable: false),
                CreatedByIp = table.Column<string>(
                    type: "nvarchar(45)", maxLength: 45,
                    nullable: true),
                LastModifiedBy = table.Column<Guid>(
                    type: "uniqueidentifier",
                    nullable: true),
                LastModifiedAt = table.Column<DateTime>(
                    type: "datetime2",
                    nullable: true),
                LastModifiedByIp = table.Column<string>(
                    type: "nvarchar(45)", maxLength: 45,
                    nullable: true),
                RowVersion = table.Column<byte[]>(
                    type: "rowversion",
                    rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CatalogCapabilityDefinitions", x => x.Id);
                table.ForeignKey(
                    name: "FK_CatalogCapabilityDefinitions_Modules_ModuleId",
                    column: x => x.ModuleId,
                    principalTable: "Modules",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CatalogCapabilityDefinitions_Resources_ResourceId",
                    column: x => x.ResourceId,
                    principalTable: "Resources",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "CatalogPlanVersionCapabilityValues",
            columns: table => new
            {
                Id = table.Column<Guid>(
                    type: "uniqueidentifier",
                    nullable: false),
                PlanVersionId = table.Column<Guid>(
                    type: "uniqueidentifier",
                    nullable: false),
                CapabilityDefinitionId = table.Column<Guid>(
                    type: "uniqueidentifier",
                    nullable: false),
                BooleanValue = table.Column<bool>(
                    type: "bit",
                    nullable: true),
                IntegerValue = table.Column<long>(
                    type: "bigint",
                    nullable: true),
                ModeSetJson = table.Column<string>(
                    type: "nvarchar(2000)", maxLength: 2000,
                    nullable: true),
                IsUnlimited = table.Column<bool>(
                    type: "bit",
                    nullable: false),
                CreatedBy = table.Column<Guid>(
                    type: "uniqueidentifier",
                    nullable: false),
                CreatedAt = table.Column<DateTime>(
                    type: "datetime2",
                    nullable: false),
                CreatedByIp = table.Column<string>(
                    type: "nvarchar(45)", maxLength: 45,
                    nullable: true),
                LastModifiedBy = table.Column<Guid>(
                    type: "uniqueidentifier",
                    nullable: true),
                LastModifiedAt = table.Column<DateTime>(
                    type: "datetime2",
                    nullable: true),
                LastModifiedByIp = table.Column<string>(
                    type: "nvarchar(45)", maxLength: 45,
                    nullable: true),
                RowVersion = table.Column<byte[]>(
                    type: "rowversion",
                    rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CatalogPlanVersionCapabilityValues", x => x.Id);
                table.ForeignKey(
                    name: "FK_CatalogPlanVersionCapabilityValues_CatalogPlanVersions_PlanVersionId",
                    column: x => x.PlanVersionId,
                    principalTable: "CatalogPlanVersions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CatalogPlanVersionCapabilityValues_CatalogCapabilityDefinitions_CapabilityDefinitionId",
                    column: x => x.CapabilityDefinitionId,
                    principalTable: "CatalogCapabilityDefinitions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "CatalogPlanPriceOffers",
            columns: table => new
            {
                Id = table.Column<Guid>(
                    type: "uniqueidentifier",
                    nullable: false),
                PlanVersionId = table.Column<Guid>(
                    type: "uniqueidentifier",
                    nullable: false),
                BillingCycle = table.Column<string>(
                    type: "nvarchar(24)", maxLength: 24,
                    nullable: false),
                Currency = table.Column<string>(
                    type: "nvarchar(3)", maxLength: 3,
                    nullable: false),
                Amount = table.Column<decimal>(
                    type: "decimal(19,2)",
                    nullable: false),
                IsActive = table.Column<bool>(
                    type: "bit",
                    nullable: false),
                SaleStartsAtUtc = table.Column<DateTime>(
                    type: "datetime2",
                    nullable: true),
                SaleEndsAtUtc = table.Column<DateTime>(
                    type: "datetime2",
                    nullable: true),
                CreatedBy = table.Column<Guid>(
                    type: "uniqueidentifier",
                    nullable: false),
                CreatedAt = table.Column<DateTime>(
                    type: "datetime2",
                    nullable: false),
                CreatedByIp = table.Column<string>(
                    type: "nvarchar(45)", maxLength: 45,
                    nullable: true),
                LastModifiedBy = table.Column<Guid>(
                    type: "uniqueidentifier",
                    nullable: true),
                LastModifiedAt = table.Column<DateTime>(
                    type: "datetime2",
                    nullable: true),
                LastModifiedByIp = table.Column<string>(
                    type: "nvarchar(45)", maxLength: 45,
                    nullable: true),
                RowVersion = table.Column<byte[]>(
                    type: "rowversion",
                    rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CatalogPlanPriceOffers", x => x.Id);
                table.ForeignKey(
                    name: "FK_CatalogPlanPriceOffers_CatalogPlanVersions_PlanVersionId",
                    column: x => x.PlanVersionId,
                    principalTable: "CatalogPlanVersions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "CatalogPlanDisplayItems",
            columns: table => new
            {
                Id = table.Column<Guid>(
                    type: "uniqueidentifier",
                    nullable: false),
                PlanVersionId = table.Column<Guid>(
                    type: "uniqueidentifier",
                    nullable: false),
                Kind = table.Column<string>(
                    type: "nvarchar(20)", maxLength: 20,
                    nullable: false),
                ModuleId = table.Column<Guid>(
                    type: "uniqueidentifier",
                    nullable: true),
                CapabilityDefinitionId = table.Column<Guid>(
                    type: "uniqueidentifier",
                    nullable: true),
                Title = table.Column<string>(
                    type: "nvarchar(200)", maxLength: 200,
                    nullable: false),
                Description = table.Column<string>(
                    type: "nvarchar(1000)", maxLength: 1000,
                    nullable: true),
                SortOrder = table.Column<int>(
                    type: "int",
                    nullable: false),
                IsHighlighted = table.Column<bool>(
                    type: "bit",
                    nullable: false),
                CreatedBy = table.Column<Guid>(
                    type: "uniqueidentifier",
                    nullable: false),
                CreatedAt = table.Column<DateTime>(
                    type: "datetime2",
                    nullable: false),
                CreatedByIp = table.Column<string>(
                    type: "nvarchar(45)", maxLength: 45,
                    nullable: true),
                LastModifiedBy = table.Column<Guid>(
                    type: "uniqueidentifier",
                    nullable: true),
                LastModifiedAt = table.Column<DateTime>(
                    type: "datetime2",
                    nullable: true),
                LastModifiedByIp = table.Column<string>(
                    type: "nvarchar(45)", maxLength: 45,
                    nullable: true),
                RowVersion = table.Column<byte[]>(
                    type: "rowversion",
                    rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CatalogPlanDisplayItems", x => x.Id);
                table.ForeignKey(
                    name: "FK_CatalogPlanDisplayItems_CatalogPlanVersions_PlanVersionId",
                    column: x => x.PlanVersionId,
                    principalTable: "CatalogPlanVersions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CatalogPlanDisplayItems_Modules_ModuleId",
                    column: x => x.ModuleId,
                    principalTable: "Modules",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CatalogPlanDisplayItems_CatalogCapabilityDefinitions_CapabilityDefinitionId",
                    column: x => x.CapabilityDefinitionId,
                    principalTable: "CatalogCapabilityDefinitions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });


        migrationBuilder.CreateIndex(
            name: "IX_CatalogPlans_CreatedAt",
            table: "CatalogPlans", column: "CreatedAt", descending: new[] { true });
        migrationBuilder.CreateIndex(
            name: "IX_CatalogPlans_Code",
            table: "CatalogPlans",
            columns: "Code",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_CatalogPlans_ParentPlanId",
            table: "CatalogPlans",
            columns: "ParentPlanId");

        migrationBuilder.CreateIndex(
            name: "IX_CatalogPlanVersions_CreatedAt",
            table: "CatalogPlanVersions", column: "CreatedAt", descending: new[] { true });
        migrationBuilder.CreateIndex(
            name: "IX_CatalogPlanVersions_PlanId_VersionNumber",
            table: "CatalogPlanVersions",
            columns: new[] { "PlanId", "VersionNumber" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_CatalogPlanVersions_ParentPlanVersionId",
            table: "CatalogPlanVersions",
            columns: "ParentPlanVersionId");

        migrationBuilder.CreateIndex(
            name: "IX_CatalogPlanVersions_Status_DisplayOrder",
            table: "CatalogPlanVersions",
            columns: new[] { "Status", "DisplayOrder" });

        migrationBuilder.CreateIndex(
            name: "IX_CatalogPlanVersionModules_CreatedAt",
            table: "CatalogPlanVersionModules", column: "CreatedAt", descending: new[] { true });
        migrationBuilder.CreateIndex(
            name: "IX_CatalogPlanVersionModules_PlanVersionId_ModuleId",
            table: "CatalogPlanVersionModules",
            columns: new[] { "PlanVersionId", "ModuleId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_CatalogPlanVersionModules_ModuleId",
            table: "CatalogPlanVersionModules",
            columns: "ModuleId");

        migrationBuilder.CreateIndex(
            name: "IX_CatalogCapabilityDefinitions_CreatedAt",
            table: "CatalogCapabilityDefinitions", column: "CreatedAt", descending: new[] { true });
        migrationBuilder.CreateIndex(
            name: "IX_CatalogCapabilityDefinitions_Code",
            table: "CatalogCapabilityDefinitions",
            columns: "Code",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_CatalogCapabilityDefinitions_ModuleId",
            table: "CatalogCapabilityDefinitions",
            columns: "ModuleId");

        migrationBuilder.CreateIndex(
            name: "IX_CatalogCapabilityDefinitions_ResourceId",
            table: "CatalogCapabilityDefinitions",
            columns: "ResourceId");

        migrationBuilder.CreateIndex(
            name: "IX_CatalogPlanVersionCapabilityValues_CreatedAt",
            table: "CatalogPlanVersionCapabilityValues", column: "CreatedAt", descending: new[] { true });
        migrationBuilder.CreateIndex(
            name: "IX_CatalogPlanVersionCapabilityValues_PlanVersionId_CapabilityDefinitionId",
            table: "CatalogPlanVersionCapabilityValues",
            columns: new[] { "PlanVersionId", "CapabilityDefinitionId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_CatalogPlanVersionCapabilityValues_CapabilityDefinitionId",
            table: "CatalogPlanVersionCapabilityValues",
            columns: "CapabilityDefinitionId");

        migrationBuilder.CreateIndex(
            name: "IX_CatalogPlanPriceOffers_CreatedAt",
            table: "CatalogPlanPriceOffers", column: "CreatedAt", descending: new[] { true });
        migrationBuilder.CreateIndex(
            name: "IX_CatalogPlanPriceOffers_PlanVersionId_BillingCycle_Currency",
            table: "CatalogPlanPriceOffers",
            columns: new[] { "PlanVersionId", "BillingCycle", "Currency" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_CatalogPlanDisplayItems_CreatedAt",
            table: "CatalogPlanDisplayItems", column: "CreatedAt", descending: new[] { true });
        migrationBuilder.CreateIndex(
            name: "IX_CatalogPlanDisplayItems_PlanVersionId_SortOrder",
            table: "CatalogPlanDisplayItems",
            columns: new[] { "PlanVersionId", "SortOrder" });

        migrationBuilder.CreateIndex(
            name: "IX_CatalogPlanDisplayItems_ModuleId",
            table: "CatalogPlanDisplayItems",
            columns: "ModuleId");

        migrationBuilder.CreateIndex(
            name: "IX_CatalogPlanDisplayItems_CapabilityDefinitionId",
            table: "CatalogPlanDisplayItems",
            columns: "CapabilityDefinitionId");

        migrationBuilder.AddCheckConstraint(
            name: "CK_CatalogPlans_Code", table: "CatalogPlans",
            sql: "LEN([Code]) > 0");
        migrationBuilder.AddCheckConstraint(
            name: "CK_CatalogPlans_Parent", table: "CatalogPlans",
            sql: "[ParentPlanId] IS NULL OR [ParentPlanId] <> [Id]");
        migrationBuilder.AddCheckConstraint(
            name: "CK_CatalogPlanVersions_Version", table: "CatalogPlanVersions",
            sql: "[VersionNumber] > 0");
        migrationBuilder.AddCheckConstraint(
            name: "CK_CatalogPlanVersions_Status", table: "CatalogPlanVersions",
            sql: "[Status] IN ('Draft','Published','Retired')");
        migrationBuilder.AddCheckConstraint(
            name: "CK_CatalogPlanVersions_PublishedAt", table: "CatalogPlanVersions",
            sql: "[Status] <> 'Published' OR [PublishedAtUtc] IS NOT NULL");
        migrationBuilder.AddCheckConstraint(
            name: "CK_CatalogCapabilityDefinitions_Kind", table: "CatalogCapabilityDefinitions",
            sql: "[Kind] IN ('Feature','CountLimit','PeriodQuota','AllowedModes')");
        migrationBuilder.AddCheckConstraint(
            name: "CK_CatalogCapabilityDefinitions_ValueKind", table: "CatalogCapabilityDefinitions",
            sql: "[ValueKind] IN ('Boolean','Integer','ModeSet')");
        migrationBuilder.AddCheckConstraint(
            name: "CK_CatalogCapabilityDefinitions_Merge", table: "CatalogCapabilityDefinitions",
            sql: "[MergePolicy] IN ('Override','Additive','Maximum','SetUnion','DenyWins')");
        migrationBuilder.AddCheckConstraint(
            name: "CK_CatalogPlanVersionCapabilityValues_ExactlyOne", table: "CatalogPlanVersionCapabilityValues",
            sql: "(CASE WHEN [BooleanValue] IS NOT NULL THEN 1 ELSE 0 END + CASE WHEN [IntegerValue] IS NOT NULL THEN 1 ELSE 0 END + CASE WHEN [ModeSetJson] IS NOT NULL THEN 1 ELSE 0 END + CASE WHEN [IsUnlimited] = 1 THEN 1 ELSE 0 END) = 1");
        migrationBuilder.AddCheckConstraint(
            name: "CK_CatalogPlanVersionCapabilityValues_NonNegative", table: "CatalogPlanVersionCapabilityValues",
            sql: "[IntegerValue] IS NULL OR [IntegerValue] >= 0");
        migrationBuilder.AddCheckConstraint(
            name: "CK_CatalogPlanPriceOffers_Amount", table: "CatalogPlanPriceOffers",
            sql: "[Amount] >= 0");
        migrationBuilder.AddCheckConstraint(
            name: "CK_CatalogPlanPriceOffers_Dates", table: "CatalogPlanPriceOffers",
            sql: "[SaleEndsAtUtc] IS NULL OR [SaleStartsAtUtc] IS NULL OR [SaleEndsAtUtc] > [SaleStartsAtUtc]");
        migrationBuilder.AddCheckConstraint(
            name: "CK_CatalogPlanDisplayItems_Kind", table: "CatalogPlanDisplayItems",
            sql: "[Kind] IN ('Module','Capability','Benefit')");
        migrationBuilder.AddCheckConstraint(
            name: "CK_CatalogPlanDisplayItems_Owner", table: "CatalogPlanDisplayItems",
            sql: "([Kind] = 'Module' AND [ModuleId] IS NOT NULL AND [CapabilityDefinitionId] IS NULL) OR ([Kind] = 'Capability' AND [CapabilityDefinitionId] IS NOT NULL AND [ModuleId] IS NULL) OR ([Kind] = 'Benefit' AND [ModuleId] IS NULL AND [CapabilityDefinitionId] IS NULL)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "CatalogPlanDisplayItems");
        migrationBuilder.DropTable(name: "CatalogPlanPriceOffers");
        migrationBuilder.DropTable(name: "CatalogPlanVersionCapabilityValues");
        migrationBuilder.DropTable(name: "CatalogCapabilityDefinitions");
        migrationBuilder.DropTable(name: "CatalogPlanVersionModules");
        migrationBuilder.DropTable(name: "CatalogPlanVersions");
        migrationBuilder.DropTable(name: "CatalogPlans");
    }
}
