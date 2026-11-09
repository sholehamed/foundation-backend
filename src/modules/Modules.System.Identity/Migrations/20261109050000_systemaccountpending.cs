using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

#nullable disable
namespace Modules.Identity.Migrations;

/// <summary>
/// FR-14B-2A pending System accounts: immutable, credential-free lifecycle
/// event. No FK to Users so audit survives an eventual account deletion.
/// Schema upgrade must be separately reviewed before enabling the HTTP flag.
/// </summary>
[DbContext(typeof(IdentityDbContext))]
[Migration("20261109050000_systemaccountpending")]
public sealed class systemaccountpending : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // A TenantId-null account must have a unique normalized email too.
        // No backfill/automatic repair: existing duplicates require explicit
        // operator review before deployment.
        migrationBuilder.Sql("""
            IF EXISTS (
                SELECT [NormalizedEmail] FROM [dbo].[Users]
                WHERE [TenantId] IS NULL AND [UserType] = 1
                  AND [NormalizedEmail] IS NOT NULL
                GROUP BY [NormalizedEmail] HAVING COUNT(*) > 1
            )
                THROW 51081,
                    'Duplicate System emails require review before FR-14B-2A migration.', 1;
            """);
        migrationBuilder.Sql("""
            CREATE UNIQUE INDEX [UX_Users_System_NormalizedEmail]
            ON [dbo].[Users] ([NormalizedEmail])
            WHERE [TenantId] IS NULL AND [NormalizedEmail] IS NOT NULL;
            """);

        migrationBuilder.CreateTable(
            name: "SystemAccountLifecycleAuditEntries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TargetUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Action = table.Column<string>(type: "nvarchar(40)", maxLength: 40,
                    nullable: false),
                OccurredAtUtc = table.Column<DateTime>(type: "datetime2(7)",
                    nullable: false, defaultValueSql: "SYSUTCDATETIME()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SystemAccountLifecycleAuditEntries", x => x.Id);
                table.CheckConstraint("CK_SystemAccountLifecycleAuditEntries_Action",
                    "[Action] IN ('create_inactive')");
                table.CheckConstraint("CK_SystemAccountLifecycleAuditEntries_Actor",
                    "[ActorUserId] <> '00000000-0000-0000-0000-000000000000'");
                table.CheckConstraint("CK_SystemAccountLifecycleAuditEntries_Target",
                    "[TargetUserId] <> '00000000-0000-0000-0000-000000000000'");
            });
        migrationBuilder.CreateIndex("IX_SystemAccountLifecycleAuditEntries_Target",
            "SystemAccountLifecycleAuditEntries",
            new[] { "TargetUserId", "OccurredAtUtc" });

        // Reject UPDATE/DELETE even if EF is bypassed. DBAs can disable
        // triggers; this is append-only, not tamper-evident event sourcing.
        migrationBuilder.Sql("""
            CREATE TRIGGER [dbo].[TR_SystemAccountLifecycleAuditEntries_AppendOnly]
            ON [dbo].[SystemAccountLifecycleAuditEntries]
            AFTER UPDATE, DELETE
            AS
            BEGIN
                SET NOCOUNT ON;
                THROW 51082, 'System account lifecycle audit is append-only.', 1;
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS
                [dbo].[TR_SystemAccountLifecycleAuditEntries_AppendOnly]
            """);
        migrationBuilder.DropTable(name: "SystemAccountLifecycleAuditEntries");
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS [UX_Users_System_NormalizedEmail] ON [dbo].[Users]
            """);
    }
}
