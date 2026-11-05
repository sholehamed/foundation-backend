using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

#nullable disable

namespace Modules.Identity.Migrations;

/// <summary>
/// SQL-owned append-only ledger for reviewed platform grant operations.
/// It intentionally has no EF DbSet and cannot be mutated using SaveChanges.
/// No FK to Users/Groups/Roles: audit history must survive their deletion.
/// </summary>
[DbContext(typeof(IdentityDbContext))]
[Migration("20261023110000_platformgrantaudit")]
public sealed class platformgrantaudit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PlatformGrantAuditEntries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RelatedId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Action = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                PreviousGrant = table.Column<bool>(type: "bit", nullable: true),
                CurrentGrant = table.Column<bool>(type: "bit", nullable: true),
                OccurredAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false,
                    defaultValueSql: "SYSUTCDATETIME()")
            },
            constraints: table => table.PrimaryKey("PK_PlatformGrantAuditEntries", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_PlatformGrantAuditEntries_Target",
            table: "PlatformGrantAuditEntries",
            columns: new[] { "TenantId", "Kind", "TargetId", "OccurredAtUtc" });

        // Application-level code cannot UPDATE or DELETE this history; SQL Server
        // also rejects DML even when bypassing EF. DDL-level attackers/DBAs can
        // disable triggers: this is durable append-only, NOT tamper-evident.
        migrationBuilder.Sql("""
            CREATE TRIGGER [dbo].[TR_PlatformGrantAuditEntries_AppendOnly]
            ON [dbo].[PlatformGrantAuditEntries]
            AFTER UPDATE, DELETE
            AS
            BEGIN
                SET NOCOUNT ON;
                THROW 51001, 'Platform grant audit history is append-only.', 1;
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS [dbo].[TR_PlatformGrantAuditEntries_AppendOnly]");
        migrationBuilder.DropTable(name: "PlatformGrantAuditEntries");
    }
}
