using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

#nullable disable
namespace Modules.Identity.Migrations;

/// <summary>
/// FR-14B-2B: challenge-backed System first login. Separate from tenant
/// credentials because System accounts have TenantId=null and no tenant slug.
/// Only disposable SQL CI can apply the migration until deployment is approved.
/// </summary>
[DbContext(typeof(IdentityDbContext))]
[Migration("20261109060000_systemfirstlogin")]
public sealed class systemfirstlogin : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_SystemAccountLifecycleAuditEntries_Action",
            table: "SystemAccountLifecycleAuditEntries");
        migrationBuilder.AddCheckConstraint(
            name: "CK_SystemAccountLifecycleAuditEntries_Action",
            table: "SystemAccountLifecycleAuditEntries",
            sql: "[Action] IN ('create_inactive','temp_issued','temp_verified','temp_activated')");

        migrationBuilder.CreateTable(
            name: "SystemAccountTemporaryCredentials",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TargetUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                IssuedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                ExpiresAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                UsedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                RevokedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                FailedAttempts = table.Column<int>(type: "int", nullable: false, defaultValue: 0)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SystemAccountTemporaryCredentials", x => x.Id);
                table.CheckConstraint("CK_SystemAccountTemporaryCredentials_Expiry",
                    "[ExpiresAtUtc] > [CreatedAtUtc]");
                table.CheckConstraint("CK_SystemAccountTemporaryCredentials_Attempts",
                    "[FailedAttempts] >= 0");
            });
        migrationBuilder.CreateIndex(
            name: "IX_SystemAccountTemporaryCredentials_Target",
            table: "SystemAccountTemporaryCredentials",
            columns: new[] { "TargetUserId", "CreatedAtUtc" });

        migrationBuilder.CreateTable(
            name: "SystemAccountFirstLoginChallenges",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TargetUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TemporaryCredentialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TokenHash = table.Column<byte[]>(type: "varbinary(32)", maxLength: 32, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                ExpiresAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                UsedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                RevokedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_SystemAccountFirstLoginChallenges", x => x.Id);
                table.CheckConstraint("CK_SystemAccountFirstLoginChallenges_Expiry",
                    "[ExpiresAtUtc] > [CreatedAtUtc]");
                table.ForeignKey("FK_SystemAccountFirstLoginChallenges_Temp",
                    x => x.TemporaryCredentialId, "SystemAccountTemporaryCredentials", "Id",
                    onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex(
            name: "UX_SystemAccountFirstLoginChallenges_TokenHash",
            table: "SystemAccountFirstLoginChallenges",
            column: "TokenHash", unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_SystemAccountFirstLoginChallenges_Target",
            table: "SystemAccountFirstLoginChallenges",
            columns: new[] { "TargetUserId", "CreatedAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Deliberately non-destructive rollback guard: a deployed credential
        // or activation must not silently lose its one-time audit history.
        migrationBuilder.Sql("""
            IF EXISTS (
                SELECT 1 FROM [dbo].[SystemAccountLifecycleAuditEntries]
                WHERE [Action] IN ('temp_issued','temp_verified','temp_activated')
            )
                THROW 51084,
                    'Reviewed data retention required before System first-login rollback.', 1;
            """);

        migrationBuilder.DropTable(name: "SystemAccountFirstLoginChallenges");
        migrationBuilder.DropTable(name: "SystemAccountTemporaryCredentials");
        migrationBuilder.DropCheckConstraint(
            name: "CK_SystemAccountLifecycleAuditEntries_Action",
            table: "SystemAccountLifecycleAuditEntries");
        migrationBuilder.AddCheckConstraint(
            name: "CK_SystemAccountLifecycleAuditEntries_Action",
            table: "SystemAccountLifecycleAuditEntries",
            sql: "[Action] IN ('create_inactive')");
    }
}
