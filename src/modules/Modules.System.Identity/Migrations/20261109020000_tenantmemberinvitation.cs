using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

#nullable disable
namespace Modules.Identity.Migrations;

/// <summary>
/// SQL Server-only single-use invitation state; never stores plaintext token.
/// Lifecycle events are appended to existing TenantMemberLifecycleAuditEntries.
/// </summary>
[DbContext(typeof(IdentityDbContext))]
[Migration("20261109020000_tenantmemberinvitation")]
public sealed class tenantmemberinvitation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TenantMemberInvitations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TargetUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                IssuedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TokenHash = table.Column<byte[]>(type: "varbinary(32)", maxLength: 32, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                ExpiresAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                SubmittedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                UsedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                RevokedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TenantMemberInvitations", x => x.Id);
                table.CheckConstraint("CK_TenantMemberInvitations_Expiry",
                    "[ExpiresAtUtc] > [CreatedAtUtc]");
            });
        migrationBuilder.CreateIndex(
            name: "UX_TenantMemberInvitations_TokenHash",
            table: "TenantMemberInvitations", column: "TokenHash", unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_TenantMemberInvitations_TenantUserCreated",
            table: "TenantMemberInvitations",
            columns: new[] { "TenantId", "TargetUserId", "CreatedAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "TenantMemberInvitations");
    }
}
