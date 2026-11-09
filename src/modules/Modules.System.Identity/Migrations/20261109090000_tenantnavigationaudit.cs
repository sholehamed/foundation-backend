using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

#nullable disable

namespace Modules.Identity.Migrations;

/// <summary>
/// FR-14C-2B: immutable Tenant menu mutation ledger. The parent catalog
/// migration (20261109080000) remains additive and not deployed.
/// </summary>
[DbContext(typeof(IdentityDbContext))]
[Migration("20261109090000_tenantnavigationaudit")]
public sealed class TenantNavigationAuditMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE [dbo].[TenantNavigationAuditEntries] (
                [Id] uniqueidentifier NOT NULL,
                [TenantId] uniqueidentifier NOT NULL,
                [ActorUserId] uniqueidentifier NOT NULL,
                [EntityId] uniqueidentifier NOT NULL,
                [EntityType] varchar(10) NOT NULL,
                [Action] varchar(30) NOT NULL,
                [OccurredAtUtc] datetime2(7) NOT NULL
                    CONSTRAINT [DF_TenantNavigationAudit_When] DEFAULT SYSUTCDATETIME(),
                [PreviousVersion] binary(8) NULL,
                [CurrentVersion] binary(8) NULL,
                CONSTRAINT [PK_TenantNavigationAuditEntries] PRIMARY KEY ([Id]),
                CONSTRAINT [FK_TenantNavigationAudit_Tenant]
                    FOREIGN KEY ([TenantId]) REFERENCES [dbo].[Tenants]([Id])
                    ON DELETE NO ACTION,
                CONSTRAINT [CK_TenantNavigationAudit_Type]
                    CHECK ([EntityType] IN ('category','item')),
                CONSTRAINT [CK_TenantNavigationAudit_Action]
                    CHECK ([Action] IN (
                        'category_created','category_updated',
                        'item_created','item_updated'))
            );
            CREATE INDEX [IX_TenantNavigationAudit_TenantTime]
                ON [dbo].[TenantNavigationAuditEntries]
                    ([TenantId], [OccurredAtUtc] DESC, [Id]);
            """);
        migrationBuilder.Sql("""
            CREATE TRIGGER [dbo].[TR_TenantNavigationAudit_AppendOnly]
            ON [dbo].[TenantNavigationAuditEntries]
            AFTER UPDATE, DELETE AS
            BEGIN
                SET NOCOUNT ON;
                THROW 51091, 'Tenant navigation audit is append-only.', 1;
            END
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [dbo].[TenantNavigationAuditEntries])
                THROW 51092,
                    'Tenant navigation audit retention requires operator review before rollback.', 1;
            DROP TRIGGER [dbo].[TR_TenantNavigationAudit_AppendOnly];
            DROP TABLE [dbo].[TenantNavigationAuditEntries];
            """);
    }
}
