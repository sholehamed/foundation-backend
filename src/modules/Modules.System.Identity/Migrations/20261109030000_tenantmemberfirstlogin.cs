using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

#nullable disable
namespace Modules.Identity.Migrations;

/// <summary>
/// No plaintext initial credentials or unrestricted login sessions are stored.
/// The challenge is NOT an OAuth token; it is accepted only by the dedicated
/// password change endpoint and bound to one initial credential row.
/// </summary>
[DbContext(typeof(IdentityDbContext))]
[Migration("20261109030000_tenantmemberfirstlogin")]
public sealed class tenantmemberfirstlogin : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("TenantMemberTemporaryCredentials", columns: table => new
        {
            Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            TargetUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            IssuedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
            CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
            ExpiresAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
            UsedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
            RevokedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
            FailedAttempts = table.Column<int>(type: "int", nullable: false, defaultValue: 0)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_TenantMemberTemporaryCredentials", x => x.Id);
            table.CheckConstraint("CK_TenantMemberTemporaryCredentials_Expiry", "[ExpiresAtUtc] > [CreatedAtUtc]");
            table.CheckConstraint("CK_TenantMemberTemporaryCredentials_Attempts", "[FailedAttempts] >= 0");
        });
        migrationBuilder.CreateIndex("IX_TenantMemberTemporaryCredentials_TenantTarget",
            "TenantMemberTemporaryCredentials", new[] { "TenantId", "TargetUserId", "CreatedAtUtc" });

        migrationBuilder.CreateTable("TenantMemberFirstLoginChallenges", columns: table => new
        {
            Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            TargetUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            TemporaryCredentialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            TokenHash = table.Column<byte[]>(type: "varbinary(32)", maxLength: 32, nullable: false),
            CreatedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
            ExpiresAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
            UsedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
            RevokedAtUtc = table.Column<DateTime>(type: "datetime2(7)", nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_TenantMemberFirstLoginChallenges", x => x.Id);
            table.CheckConstraint("CK_TenantMemberFirstLoginChallenges_Expiry", "[ExpiresAtUtc] > [CreatedAtUtc]");
            table.ForeignKey("FK_TenantMemberFirstLoginChallenges_TempCredential",
                x => x.TemporaryCredentialId, "TenantMemberTemporaryCredentials", "Id",
                onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex("UX_TenantMemberFirstLoginChallenges_TokenHash",
            "TenantMemberFirstLoginChallenges", "TokenHash", unique: true);
        migrationBuilder.CreateIndex("IX_TenantMemberFirstLoginChallenges_TenantTarget",
            "TenantMemberFirstLoginChallenges", new[] { "TenantId", "TargetUserId", "CreatedAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("TenantMemberFirstLoginChallenges");
        migrationBuilder.DropTable("TenantMemberTemporaryCredentials");
    }
}
