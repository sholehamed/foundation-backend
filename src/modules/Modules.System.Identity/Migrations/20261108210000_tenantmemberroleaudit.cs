using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

#nullable disable
namespace Modules.Identity.Migrations;

/// <summary>
/// Append-only ledger for reviewed Tenant member-role grants; no FK to
/// users/roles so history survives deletion. This is not tamper-evident.
/// </summary>
[DbContext(typeof(IdentityDbContext))]
[Migration("20261108210000_tenantmemberroleaudit")]
public sealed class tenantmemberroleaudit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TenantMemberRoleAuditEntries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TargetUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RoleName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Action = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                OccurredAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false,
                    defaultValueSql: "SYSUTCDATETIME()")
            },
            constraints: table => table.PrimaryKey("PK_TenantMemberRoleAuditEntries", x => x.Id));
        migrationBuilder.CreateIndex(
            name: "IX_TenantMemberRoleAuditEntries_TenantTargetAt",
            table: "TenantMemberRoleAuditEntries",
            columns: new[] { "TenantId", "TargetUserId", "OccurredAtUtc" });
        migrationBuilder.Sql("""
            CREATE TRIGGER [dbo].[TR_TenantMemberRoleAuditEntries_AppendOnly]
            ON [dbo].[TenantMemberRoleAuditEntries]
            AFTER UPDATE, DELETE
            AS BEGIN
                SET NOCOUNT ON;
                THROW 51051, 'Tenant membership audit is append-only.', 1;
            END
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "DROP TRIGGER IF EXISTS [dbo].[TR_TenantMemberRoleAuditEntries_AppendOnly]");
        migrationBuilder.DropTable(name: "TenantMemberRoleAuditEntries");
    }
}
