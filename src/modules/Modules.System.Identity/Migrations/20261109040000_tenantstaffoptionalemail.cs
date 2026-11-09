using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

#nullable disable
namespace Modules.Identity.Migrations;

/// <summary>
/// FR-13B-3A: email may be missing only for locally created Tenant staff.
/// Public Tenant Owner and System user creation still validate email.
/// SQL Server's unique email index MUST exclude NULL, or one NULL email per
/// Tenant would block the second employee.
/// </summary>
[DbContext(typeof(IdentityDbContext))]
[Migration("20261109040000_tenantstaffoptionalemail")]
public sealed class tenantstaffoptionalemail : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Users_TenantId_NormalizedEmail", table: "Users");
        migrationBuilder.AlterColumn<string>(
            name: "Email", table: "Users", type: "nvarchar(256)",
            maxLength: 256, nullable: true,
            oldClrType: typeof(string), oldType: "nvarchar(256)",
            oldMaxLength: 256);
        migrationBuilder.AlterColumn<string>(
            name: "NormalizedEmail", table: "Users", type: "nvarchar(256)",
            maxLength: 256, nullable: true,
            oldClrType: typeof(string), oldType: "nvarchar(256)",
            oldMaxLength: 256);
        migrationBuilder.CreateIndex(
            name: "IX_Users_TenantId_NormalizedEmail",
            table: "Users",
            columns: new[] { "TenantId", "NormalizedEmail" },
            unique: true,
            filter: "[TenantId] IS NOT NULL AND [NormalizedEmail] IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Avoid fabricating email addresses, and avoid destructive rollback:
        // an operator must first genuinely supply emails for email-less
        // accounts before this stricter schema can be reinstated.
        migrationBuilder.Sql("""
            IF EXISTS(SELECT 1 FROM [dbo].[Users]
                      WHERE [Email] IS NULL OR [NormalizedEmail] IS NULL)
                THROW 51078,
                    'Cannot reverse nullable staff email while email-less users exist.', 1;
            """);

        migrationBuilder.DropIndex(
            name: "IX_Users_TenantId_NormalizedEmail", table: "Users");
        migrationBuilder.AlterColumn<string>(
            name: "Email", table: "Users", type: "nvarchar(256)",
            maxLength: 256, nullable: false,
            oldClrType: typeof(string), oldType: "nvarchar(256)",
            oldMaxLength: 256, oldNullable: true);
        migrationBuilder.AlterColumn<string>(
            name: "NormalizedEmail", table: "Users", type: "nvarchar(256)",
            maxLength: 256, nullable: false,
            oldClrType: typeof(string), oldType: "nvarchar(256)",
            oldMaxLength: 256, oldNullable: true);
        migrationBuilder.CreateIndex(
            name: "IX_Users_TenantId_NormalizedEmail",
            table: "Users",
            columns: new[] { "TenantId", "NormalizedEmail" },
            unique: true, filter: "[TenantId] IS NOT NULL");
    }
}
