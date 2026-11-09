using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

#nullable disable

namespace Modules.Identity.Migrations;

/// <summary>Immutable, tenant-scoped UserGroup grant/revoke history.</summary>
[DbContext(typeof(IdentityDbContext))]
[Migration("20261109060000_tenantgroupmembershipaudit")]
public sealed class tenantgroupmembershipaudit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TenantGroupMembershipAuditEntries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                GroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TargetUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Action = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                OccurredAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false,
                    defaultValueSql: "SYSUTCDATETIME()")
            },
            constraints: table => table.PrimaryKey("PK_TenantGroupMembershipAuditEntries", x => x.Id));
        migrationBuilder.CreateIndex(
            name: "IX_TenantGroupMembershipAuditEntries_TenantGroupAt",
            table: "TenantGroupMembershipAuditEntries",
            columns: new[] { "TenantId", "GroupId", "OccurredAtUtc" });
        migrationBuilder.Sql("""
            CREATE TRIGGER [dbo].[TR_TenantGroupMembershipAuditEntries_AppendOnly]
            ON [dbo].[TenantGroupMembershipAuditEntries]
            AFTER UPDATE, DELETE
            AS BEGIN
                SET NOCOUNT ON;
                THROW 51072, 'Tenant group membership audit is append-only.', 1;
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "DROP TRIGGER IF EXISTS [dbo].[TR_TenantGroupMembershipAuditEntries_AppendOnly]");
        migrationBuilder.DropTable(name: "TenantGroupMembershipAuditEntries");
    }
}
