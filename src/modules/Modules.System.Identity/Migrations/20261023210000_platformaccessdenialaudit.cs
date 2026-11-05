using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

#nullable disable

namespace Modules.Identity.Migrations;

/// <summary>
/// Independently committed security decisions, separate from successful grant
/// mutations. Never part of a failed command transaction or EF change tracker.
/// Stored route templates, not client-controlled URLs; no headers or token data.
/// </summary>
[DbContext(typeof(IdentityDbContext))]
[Migration("20261023210000_platformaccessdenialaudit")]
public sealed class platformaccessdenialaudit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PlatformAccessDenialAuditEntries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                HttpMethod = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                RouteTemplate = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                Outcome = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                OccurredAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false,
                    defaultValueSql: "SYSUTCDATETIME()")
            }, constraints: table =>
                table.PrimaryKey("PK_PlatformAccessDenialAuditEntries", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_PlatformAccessDenialAuditEntries_ActorTime",
            table: "PlatformAccessDenialAuditEntries",
            columns: new[] { "TenantId", "ActorUserId", "OccurredAtUtc" });

        // SQL Server enforces append-only DML even for callers bypassing EF.
        // A DBA can alter the trigger: this is NOT an off-database sealed ledger.
        migrationBuilder.Sql("""
            CREATE TRIGGER [dbo].[TR_PlatformAccessDenialAuditEntries_AppendOnly]
            ON [dbo].[PlatformAccessDenialAuditEntries]
            AFTER UPDATE, DELETE
            AS
            BEGIN
                SET NOCOUNT ON;
                THROW 51002, 'Platform access denial audit is append-only.', 1;
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "DROP TRIGGER IF EXISTS [dbo].[TR_PlatformAccessDenialAuditEntries_AppendOnly]");
        migrationBuilder.DropTable(name: "PlatformAccessDenialAuditEntries");
    }
}
