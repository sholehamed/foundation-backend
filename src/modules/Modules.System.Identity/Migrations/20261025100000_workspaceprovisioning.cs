using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

#nullable disable

namespace Modules.Identity.Migrations;

[DbContext(typeof(IdentityDbContext))]
[Migration("20261025100000_workspaceprovisioning")]
public sealed class workspaceprovisioning : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "BusinessProfiles",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DisplayName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                ActivityType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                Phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                City = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                Address = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                PostalCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                Instagram = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                Culture = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                Timezone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                RequestedPlanKey = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                RequestedBillingCycle = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                PromoCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                AcceptedTerms = table.Column<bool>(type: "bit", nullable: false),
                AcceptedPrivacy = table.Column<bool>(type: "bit", nullable: false),
                AcceptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedByIp = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                LastModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                LastModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                LastModifiedByIp = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BusinessProfiles", x => x.Id);
                table.ForeignKey("FK_BusinessProfiles_Tenants_TenantId", x => x.TenantId,
                    "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_BusinessProfiles_CreatedAt",
            "BusinessProfiles", "CreatedAt", descending: new[] { true });
        migrationBuilder.CreateIndex("IX_BusinessProfiles_TenantId", "BusinessProfiles", "TenantId", unique: true);

        migrationBuilder.CreateTable(
            name: "Branches",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                Phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                City = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                Address = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                IsMain = table.Column<bool>(type: "bit", nullable: false),
                CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                CreatedByIp = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                LastModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                LastModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                LastModifiedByIp = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Branches", x => x.Id);
                table.ForeignKey("FK_Branches_Tenants_TenantId", x => x.TenantId,
                    "Tenants", "Id", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_Branches_CreatedAt", "Branches", "CreatedAt", descending: new[] { true });
        migrationBuilder.CreateIndex("IX_Branches_TenantId_IsMain", "Branches",
            new[] { "TenantId", "IsMain" }, unique: true, filter: "[IsMain] = 1");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "Branches");
        migrationBuilder.DropTable(name: "BusinessProfiles");
    }
}
