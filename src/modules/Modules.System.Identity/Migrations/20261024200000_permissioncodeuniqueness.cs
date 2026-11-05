using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

#nullable disable

namespace Modules.Identity.Migrations;

/// <summary>
/// Enforces normalized, global Permission.Code uniqueness at the SQL Server
/// boundary. Fail-closed preflight: never pick winners or rewrite existing
/// Permission IDs referenced by platform/tenant grants.
/// </summary>
[DbContext(typeof(IdentityDbContext))]
[Migration("20261024200000_permissioncodeuniqueness")]
public sealed class permissioncodeuniqueness : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // SQL expression is deliberately identical in preflight and persisted
        // computed key. All legacy duplicates must be reviewed and repaired
        // before rollout; the migration NEVER auto-merges or deletes grants.
        migrationBuilder.Sql("""
            IF EXISTS (
                SELECT 1 FROM [dbo].[Permissions] WITH (TABLOCKX, HOLDLOCK)
                WHERE [Code] IS NULL
                   OR LEN(LTRIM(RTRIM([Code]))) NOT BETWEEN 1 AND 100
            )
                THROW 51020, 'Permission.Code preflight failed: NULL, blank or over 100 chars. Review data before migration.', 1;

            IF EXISTS (
                SELECT UPPER(LTRIM(RTRIM([Code]))) COLLATE Latin1_General_100_CI_AS
                FROM [dbo].[Permissions] WITH (TABLOCKX, HOLDLOCK)
                GROUP BY UPPER(LTRIM(RTRIM([Code]))) COLLATE Latin1_General_100_CI_AS
                HAVING COUNT_BIG(*) > 1
            )
                THROW 51021, 'Permission.Code preflight failed: normalized duplicate codes. Inventory grants and resolve manually.', 1;

            ALTER TABLE [dbo].[Permissions]
                ADD [NormalizedCode] AS
                    (CONVERT(nvarchar(100),
                        UPPER(LTRIM(RTRIM([Code]))) COLLATE Latin1_General_100_CI_AS))
                    PERSISTED;

            ALTER TABLE [dbo].[Permissions] WITH CHECK
                ADD CONSTRAINT [CK_Permissions_Code_Length]
                CHECK (LEN(LTRIM(RTRIM([Code]))) BETWEEN 1 AND 100);

            CREATE UNIQUE INDEX [UX_Permissions_NormalizedCode]
                ON [dbo].[Permissions] ([NormalizedCode]);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Removing this protection during a rollback can re-admit duplicates;
        // the release procedure must explicitly approve schema downgrades.
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS [UX_Permissions_NormalizedCode] ON [dbo].[Permissions];
            ALTER TABLE [dbo].[Permissions] DROP CONSTRAINT [CK_Permissions_Code_Length];
            ALTER TABLE [dbo].[Permissions] DROP COLUMN [NormalizedCode];
            """);
    }
}
