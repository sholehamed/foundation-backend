using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

#nullable disable
namespace Modules.Identity.Migrations;

/// <summary>
/// Append-only Tenant group metadata/state audit. No permission grants and no
/// FK to mutable Group/User so the ledger survives their deletion.
/// </summary>
[DbContext(typeof(IdentityDbContext))]
[Migration("20261109050000_tenantgrouplifecycleaudit")]
public sealed class tenantgrouplifecycleaudit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TenantGroupChangeAuditEntries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                GroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Action = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                PreviousName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                CurrentName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                PreviousActive = table.Column<bool>(type: "bit", nullable: true),
                CurrentActive = table.Column<bool>(type: "bit", nullable: false),
                OccurredAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false,
                    defaultValueSql: "SYSUTCDATETIME()")
            },
            constraints: table => table.PrimaryKey("PK_TenantGroupChangeAuditEntries", x => x.Id));
        migrationBuilder.CreateIndex(
            name: "IX_TenantGroupChangeAuditEntries_TenantGroupAt",
            table: "TenantGroupChangeAuditEntries",
            columns: new[] { "TenantId", "GroupId", "OccurredAtUtc" });
        migrationBuilder.Sql("""
            CREATE TRIGGER [dbo].[TR_TenantGroupChangeAuditEntries_AppendOnly]
            ON [dbo].[TenantGroupChangeAuditEntries]
            AFTER UPDATE, DELETE
            AS BEGIN
                SET NOCOUNT ON;
                THROW 51071, 'Tenant group lifecycle audit is append-only.', 1;
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "DROP TRIGGER IF EXISTS [dbo].[TR_TenantGroupChangeAuditEntries_AppendOnly]");
        migrationBuilder.DropTable(name: "TenantGroupChangeAuditEntries");
    }
}
