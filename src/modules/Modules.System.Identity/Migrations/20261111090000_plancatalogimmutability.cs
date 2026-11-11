using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

#nullable disable

namespace Modules.Identity.Migrations;

/// <summary>
/// FR-17B-2C: SQL Server DML-level immutability of published/retired commercial
/// catalog snapshots. No EF model change, no historical backfill, no grants.
/// This migration DOES NOT make privileged sysadmin/DDL actors untrusted:
/// protect database DDL/ALTER permissions operationally.
/// </summary>
[DbContext(typeof(IdentityDbContext))]
[Migration("20261111090000_plancatalogimmutability")]
public sealed class PlanCatalogImmutabilityMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        Create(migrationBuilder, "TR_CatalogPlanVersions_Immutable",
            "CatalogPlanVersions", "INSERT, UPDATE, DELETE", """
            SET NOCOUNT ON;

            -- A published revision must have been created as a Draft and
            -- reviewed by the application; direct Published/Retired inserts
            -- must not circumvent that lifecycle.
            IF EXISTS (
                SELECT 1 FROM inserted AS i
                LEFT JOIN deleted AS d ON d.Id = i.Id
                WHERE d.Id IS NULL AND i.Status <> N'Draft'
            )
                THROW 51021, 'Catalog version must be inserted as Draft.', 1;

            -- A published/retired revision cannot be edited, reverted, retired
            -- or deleted in place. A new Draft/Version must be authored instead.
            IF EXISTS (
                SELECT 1 FROM deleted AS d
                WHERE d.Status IN (N'Published', N'Retired')
            )
                THROW 51022, 'Published/retired catalog version is immutable.', 1;

            IF EXISTS (
                SELECT 1 FROM inserted AS i
                INNER JOIN deleted AS d ON d.Id = i.Id
                WHERE d.Status = N'Draft' AND
                  (i.Status NOT IN (N'Draft', N'Published') OR
                   (i.Status = N'Published' AND
                     (i.PublishedAtUtc IS NULL OR
                      LEN(ISNULL(i.ContentFingerprint, N'')) <> 64)))
            )
                THROW 51023, 'Invalid Draft publication transition.', 1;
            """);

        Create(migrationBuilder, "TR_CatalogPlans_ImmutablePublishedIdentity",
            "CatalogPlans", "UPDATE, DELETE", """
            SET NOCOUNT ON;
            -- Code, parent pin, name and sale availability are identity /
            -- lifecycle decisions. Do not change them in place once *any*
            -- revision has been published. Future suspension/retirement needs
            -- a separately audited overlay, not mutation of a sold snapshot.
            IF EXISTS (
                SELECT 1
                FROM deleted AS d
                JOIN dbo.CatalogPlanVersions AS v WITH (UPDLOCK, HOLDLOCK)
                  ON v.PlanId = d.Id
                WHERE v.Status IN (N'Published', N'Retired')
            )
                THROW 51024, 'Plan identity with a published revision is immutable.', 1;
            """);

        CreateChildGuard(migrationBuilder, "CatalogPlanVersionModules", 51025);
        CreateChildGuard(migrationBuilder, "CatalogPlanVersionCapabilityValues", 51026);
        CreateChildGuard(migrationBuilder, "CatalogPlanPriceOffers", 51027);
        CreateChildGuard(migrationBuilder, "CatalogPlanDisplayItems", 51028);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var name in new[]
        {
            "TR_CatalogPlanDisplayItems_Immutable",
            "TR_CatalogPlanPriceOffers_Immutable",
            "TR_CatalogPlanVersionCapabilityValues_Immutable",
            "TR_CatalogPlanVersionModules_Immutable",
            "TR_CatalogPlans_ImmutablePublishedIdentity",
            "TR_CatalogPlanVersions_Immutable"
        })
            migrationBuilder.Sql($"DROP TRIGGER IF EXISTS [dbo].[{name}];");
    }

    private static void CreateChildGuard(MigrationBuilder migrationBuilder,
        string table, int error)
    {
        // Both before AND after PlanVersionId are checked. Changing ownership
        // from Published to Draft cannot launder a published row into editability.
        // UPDLOCK + HOLDLOCK on the parent serializes with publication, or one
        // transaction is aborted. Any mixed multi-row statement fails atomically.
        var body = $"""
            SET NOCOUNT ON;
            IF EXISTS (
                SELECT 1 FROM (
                    SELECT PlanVersionId FROM inserted
                    UNION
                    SELECT PlanVersionId FROM deleted
                ) AS affected
                JOIN dbo.CatalogPlanVersions AS v WITH (UPDLOCK, HOLDLOCK)
                  ON v.Id = affected.PlanVersionId
                WHERE v.Status IN (N'Published', N'Retired')
            )
                THROW {error}, 'Published/retired catalog child is immutable.', 1;
            """;
        Create(migrationBuilder, $"TR_{table}_Immutable", table,
            "INSERT, UPDATE, DELETE", body);
    }

    private static void Create(MigrationBuilder migrationBuilder,
        string trigger, string table, string events, string body)
    {
        var ddl = $"""
            CREATE OR ALTER TRIGGER [dbo].[{trigger}]
            ON [dbo].[{table}]
            AFTER {events}
            AS
            BEGIN
            {body}
            END;
            """;
        // CREATE TRIGGER must start its *own* SQL Server batch. EXEC also
        // works for idempotent EF migration scripts wrapped in IF/BEGIN.
        migrationBuilder.Sql("EXEC(N'" + ddl.Replace("'", "''") + "')");
    }
}
