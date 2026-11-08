using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

#nullable disable

namespace Modules.Identity.Migrations;

/// <summary>
/// Immutable, tenant-scoped grant ledger. SQL trigger blocks UPDATE/DELETE.
/// No foreign keys: security history survives role/user deletion.
/// </summary>
[DbContext(typeof(IdentityDbContext))]
[Migration("20261108163000_tenantrolegrantaudit")]
public sealed class tenantrolegrantaudit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TenantRoleGrantAuditEntries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PermissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PermissionCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Action = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                OccurredAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false,
                    defaultValueSql: "SYSUTCDATETIME()")
            },
            constraints: table => table.PrimaryKey("PK_TenantRoleGrantAuditEntries", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_TenantRoleGrantAuditEntries_ScopeRoleAt",
            table: "TenantRoleGrantAuditEntries",
            columns: new[] { "TenantId", "RoleId", "OccurredAtUtc" });

        migrationBuilder.Sql("""
            CREATE TRIGGER [dbo].[TR_TenantRoleGrantAuditEntries_AppendOnly]
            ON [dbo].[TenantRoleGrantAuditEntries]
            AFTER UPDATE, DELETE
            AS BEGIN
                SET NOCOUNT ON;
                THROW 51031, 'Tenant grant audit is append-only.', 1;
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "DROP TRIGGER IF EXISTS [dbo].[TR_TenantRoleGrantAuditEntries_AppendOnly]");
        migrationBuilder.DropTable(name: "TenantRoleGrantAuditEntries");
    }
}
