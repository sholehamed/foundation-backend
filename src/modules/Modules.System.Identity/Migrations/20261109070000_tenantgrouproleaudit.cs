using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

#nullable disable

namespace Modules.Identity.Migrations;

/// <summary>Append-only, tenant-bound GroupRole grant/revoke record.</summary>
[DbContext(typeof(IdentityDbContext))]
[Migration("20261109070000_tenantgrouproleaudit")]
public sealed class tenantgrouproleaudit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TenantGroupRoleAuditEntries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                GroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RoleName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Action = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                OccurredAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false,
                    defaultValueSql: "SYSUTCDATETIME()")
            },
            constraints: table => table.PrimaryKey("PK_TenantGroupRoleAuditEntries", x => x.Id));
        migrationBuilder.CreateIndex(
            name: "IX_TenantGroupRoleAuditEntries_TenantGroupAt",
            table: "TenantGroupRoleAuditEntries",
            columns: new[] { "TenantId", "GroupId", "OccurredAtUtc" });
        migrationBuilder.Sql("""
            CREATE TRIGGER [dbo].[TR_TenantGroupRoleAuditEntries_AppendOnly]
            ON [dbo].[TenantGroupRoleAuditEntries]
            AFTER UPDATE, DELETE
            AS BEGIN
                SET NOCOUNT ON;
                THROW 51073, 'Tenant group role audit is append-only.', 1;
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "DROP TRIGGER IF EXISTS [dbo].[TR_TenantGroupRoleAuditEntries_AppendOnly]");
        migrationBuilder.DropTable(name: "TenantGroupRoleAuditEntries");
    }
}
