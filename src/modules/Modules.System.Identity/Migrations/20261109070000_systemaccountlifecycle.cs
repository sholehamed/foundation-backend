using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

#nullable disable

namespace Modules.Identity.Migrations;

/// <summary>
/// FR-14B-3: expand existing append-only System lifecycle ledger without
/// storing PII or authentication stamps. Never backfill/fabricate past actions.
/// </summary>
[DbContext(typeof(IdentityDbContext))]
[Migration("20261109070000_systemaccountlifecycle")]
public sealed class SystemAccountLifecycleMutationMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ChangedFields", table: "SystemAccountLifecycleAuditEntries",
            type: "nvarchar(128)", maxLength: 128, nullable: true);
        migrationBuilder.AddColumn<bool>(
            name: "PreviousIsActive", table: "SystemAccountLifecycleAuditEntries",
            type: "bit", nullable: true);
        migrationBuilder.AddColumn<bool>(
            name: "CurrentIsActive", table: "SystemAccountLifecycleAuditEntries",
            type: "bit", nullable: true);
        migrationBuilder.AddColumn<bool>(
            name: "PreviousIsLockedOut", table: "SystemAccountLifecycleAuditEntries",
            type: "bit", nullable: true);
        migrationBuilder.AddColumn<bool>(
            name: "CurrentIsLockedOut", table: "SystemAccountLifecycleAuditEntries",
            type: "bit", nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "PreviousVersion", table: "SystemAccountLifecycleAuditEntries",
            type: "nvarchar(128)", maxLength: 128, nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "CurrentVersion", table: "SystemAccountLifecycleAuditEntries",
            type: "nvarchar(128)", maxLength: 128, nullable: true);

        migrationBuilder.DropCheckConstraint(
            name: "CK_SystemAccountLifecycleAuditEntries_Action",
            table: "SystemAccountLifecycleAuditEntries");
        migrationBuilder.AddCheckConstraint(
            name: "CK_SystemAccountLifecycleAuditEntries_Action",
            table: "SystemAccountLifecycleAuditEntries",
            sql: """
                [Action] IN ('create_inactive','temp_issued',
                  'temp_verified','temp_activated',
                  'profile_updated','suspended','reactivated',
                  'locked','unlocked')
                """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Never silently destroy immutable mutation evidence by rollback.
        migrationBuilder.Sql("""
            IF EXISTS (
                SELECT 1 FROM [dbo].[SystemAccountLifecycleAuditEntries]
                WHERE [Action] IN ('profile_updated','suspended',
                                   'reactivated','locked','unlocked')
            )
                THROW 51086,
                    'System account mutation audit retention must be reviewed before rollback.', 1;
            """);

        migrationBuilder.DropCheckConstraint(
            name: "CK_SystemAccountLifecycleAuditEntries_Action",
            table: "SystemAccountLifecycleAuditEntries");
        migrationBuilder.AddCheckConstraint(
            name: "CK_SystemAccountLifecycleAuditEntries_Action",
            table: "SystemAccountLifecycleAuditEntries",
            sql: "[Action] IN ('create_inactive','temp_issued','temp_verified','temp_activated')");
        foreach (var column in new[]
        {
            "ChangedFields", "PreviousVersion", "CurrentVersion"
        })
            migrationBuilder.DropColumn(name: column,
                table: "SystemAccountLifecycleAuditEntries");
        foreach (var column in new[]
        {
            "PreviousIsActive", "CurrentIsActive",
            "PreviousIsLockedOut", "CurrentIsLockedOut"
        })
            migrationBuilder.DropColumn(name: column,
                table: "SystemAccountLifecycleAuditEntries");
    }
}
