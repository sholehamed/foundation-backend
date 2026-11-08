using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

#nullable disable

namespace Modules.Identity.Migrations;

/// <summary>
/// Append-only role lifecycle history. No FKs: history survives deletion.
/// </summary>
[DbContext(typeof(IdentityDbContext))]
[Migration("20261108190000_tenantrolechangeaudit")]
public sealed class tenantrolechangeaudit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TenantRoleChangeAuditEntries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Action = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                PreviousName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                CurrentName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                PreviousActive = table.Column<bool>(type: "bit", nullable: true),
                CurrentActive = table.Column<bool>(type: "bit", nullable: false),
                OccurredAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false,
                    defaultValueSql: "SYSUTCDATETIME()")
            },
            constraints: table => table.PrimaryKey("PK_TenantRoleChangeAuditEntries", x => x.Id));
        migrationBuilder.CreateIndex(
            name: "IX_TenantRoleChangeAuditEntries_ScopeRoleAt",
            table: "TenantRoleChangeAuditEntries",
            columns: new[] { "TenantId", "RoleId", "OccurredAtUtc" });
        migrationBuilder.Sql("""
            CREATE TRIGGER [dbo].[TR_TenantRoleChangeAuditEntries_AppendOnly]
            ON [dbo].[TenantRoleChangeAuditEntries]
            AFTER UPDATE, DELETE
            AS BEGIN
                SET NOCOUNT ON;
                THROW 51041, 'Tenant role lifecycle audit is append-only.', 1;
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS [dbo].[TR_TenantRoleChangeAuditEntries_AppendOnly]");
        migrationBuilder.DropTable(name: "TenantRoleChangeAuditEntries");
    }
}
