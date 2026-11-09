using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

#nullable disable

namespace Modules.Identity.Migrations;

/// <summary>
/// FR-14C-2: additive, isolated Tenant-owned menu catalog. The existing global
/// Menu/MenuCategory records and all historical grants remain untouched.
/// This is deliberately SQL-first and read-only: future admin write operations
/// require a separately reviewed CAS + audit API, not generic DbSet mutation.
/// </summary>
[DbContext(typeof(IdentityDbContext))]
[Migration("20261109080000_tenantownednavigation")]
public sealed class TenantOwnedNavigationMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE [dbo].[TenantNavigationCategories] (
                [TenantId] uniqueidentifier NOT NULL,
                [Id] uniqueidentifier NOT NULL,
                [Title] nvarchar(120) NOT NULL,
                [SortOrder] smallint NOT NULL CONSTRAINT [DF_TenantNavCategories_Order] DEFAULT 0,
                [IsActive] bit NOT NULL CONSTRAINT [DF_TenantNavCategories_Active] DEFAULT 1,
                [RowVersion] rowversion NOT NULL,
                [CreatedAtUtc] datetime2(7) NOT NULL
                    CONSTRAINT [DF_TenantNavCategories_Created] DEFAULT SYSUTCDATETIME(),
                CONSTRAINT [PK_TenantNavigationCategories]
                    PRIMARY KEY ([TenantId], [Id]),
                CONSTRAINT [FK_TenantNavCategories_Tenants]
                    FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants] ([Id])
                    ON DELETE NO ACTION,
                CONSTRAINT [CK_TenantNavCategories_Title]
                    CHECK (LEN(LTRIM(RTRIM([Title]))) BETWEEN 1 AND 120)
            );

            CREATE TABLE [dbo].[TenantNavigationItems] (
                [TenantId] uniqueidentifier NOT NULL,
                [Id] uniqueidentifier NOT NULL,
                [CategoryId] uniqueidentifier NOT NULL,
                [ParentId] uniqueidentifier NULL,
                [Title] nvarchar(120) NOT NULL,
                [Icon] nvarchar(80) NULL,
                [Route] nvarchar(240) NULL,
                [SortOrder] smallint NOT NULL CONSTRAINT [DF_TenantNavItems_Order] DEFAULT 0,
                [IsActive] bit NOT NULL CONSTRAINT [DF_TenantNavItems_Active] DEFAULT 1,
                [RowVersion] rowversion NOT NULL,
                [CreatedAtUtc] datetime2(7) NOT NULL
                    CONSTRAINT [DF_TenantNavItems_Created] DEFAULT SYSUTCDATETIME(),
                CONSTRAINT [PK_TenantNavigationItems]
                    PRIMARY KEY ([TenantId], [Id]),
                CONSTRAINT [AK_TenantNavItems_SameCategoryParent]
                    UNIQUE ([TenantId], [CategoryId], [Id]),
                CONSTRAINT [FK_TenantNavItems_Categories]
                    FOREIGN KEY ([TenantId], [CategoryId])
                    REFERENCES [dbo].[TenantNavigationCategories] ([TenantId], [Id])
                    ON DELETE NO ACTION,
                CONSTRAINT [FK_TenantNavItems_Parent]
                    FOREIGN KEY ([TenantId], [CategoryId], [ParentId])
                    REFERENCES [dbo].[TenantNavigationItems]
                        ([TenantId], [CategoryId], [Id])
                    ON DELETE NO ACTION,
                CONSTRAINT [CK_TenantNavItems_NotSelfParent]
                    CHECK ([ParentId] IS NULL OR [ParentId] <> [Id]),
                CONSTRAINT [CK_TenantNavItems_Title]
                    CHECK (LEN(LTRIM(RTRIM([Title]))) BETWEEN 1 AND 120),
                CONSTRAINT [CK_TenantNavItems_RouteShape]
                    CHECK ([Route] IS NULL OR (
                        [Route] LIKE '/dashboard%' AND
                        [Route] NOT LIKE '%?%' AND [Route] NOT LIKE '%#%' AND
                        [Route] NOT LIKE '%..%' AND [Route] NOT LIKE '%\%'))
            );

            CREATE INDEX [IX_TenantNavCategories_Visible]
                ON [dbo].[TenantNavigationCategories]
                    ([TenantId], [IsActive], [SortOrder], [Id]);

            CREATE INDEX [IX_TenantNavItems_Visible]
                ON [dbo].[TenantNavigationItems]
                    ([TenantId], [CategoryId], [IsActive], [ParentId], [SortOrder], [Id]);

            CREATE UNIQUE INDEX [UX_TenantNavItems_UniqueRoute]
                ON [dbo].[TenantNavigationItems] ([TenantId], [Route])
                WHERE [Route] IS NOT NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Never destroy operator-owned navigation on a rollback without an
        // explicit approved export and clean-up plan.
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [dbo].[TenantNavigationItems]) OR
               EXISTS (SELECT 1 FROM [dbo].[TenantNavigationCategories])
                THROW 51090,
                    'Tenant navigation rollback would destroy owned records; operator review required.', 1;

            DROP TABLE [dbo].[TenantNavigationItems];
            DROP TABLE [dbo].[TenantNavigationCategories];
            """);
    }
}
