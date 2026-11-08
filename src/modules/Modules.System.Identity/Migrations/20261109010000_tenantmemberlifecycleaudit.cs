using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

#nullable disable
namespace Modules.Identity.Migrations;

/// <summary>
/// Append-only audit of Tenant member lifecycle writes. Contains no password,
/// invitation token, email or other credential and survives User deletion.
/// This ledger is append-only, not cryptographically tamper-evident.
/// </summary>
[DbContext(typeof(IdentityDbContext))]
[Migration("20261109010000_tenantmemberlifecycleaudit")]
public sealed class tenantmemberlifecycleaudit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TenantMemberLifecycleAuditEntries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TargetUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Action = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                OccurredAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false,
                    defaultValueSql: "SYSUTCDATETIME()")
            },
            constraints: table => table.PrimaryKey("PK_TenantMemberLifecycleAuditEntries", x => x.Id));
        migrationBuilder.CreateIndex(
            name: "IX_TenantMemberLifecycleAuditEntries_TenantMemberAt",
            table: "TenantMemberLifecycleAuditEntries",
            columns: new[] { "TenantId", "TargetUserId", "OccurredAtUtc" });
        migrationBuilder.Sql("""
            CREATE TRIGGER [dbo].[TR_TenantMemberLifecycleAuditEntries_AppendOnly]
            ON [dbo].[TenantMemberLifecycleAuditEntries]
            AFTER UPDATE, DELETE
            AS BEGIN
                SET NOCOUNT ON;
                THROW 51061, 'Tenant member lifecycle audit is append-only.', 1;
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "DROP TRIGGER IF EXISTS [dbo].[TR_TenantMemberLifecycleAuditEntries_AppendOnly]");
        migrationBuilder.DropTable(name: "TenantMemberLifecycleAuditEntries");
    }
}
