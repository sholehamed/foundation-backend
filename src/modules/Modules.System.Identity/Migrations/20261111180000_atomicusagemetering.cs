using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

#nullable disable

namespace Modules.Identity.Migrations;

/// <summary>
/// FR-17D-2A: SQL-owned, tenant-scoped atomic metering storage.
/// These tables are intentionally accessed by a guarded ADO.NET service, not
/// mapped to the EF entity model; the existing model snapshot is unchanged.
/// No commercial Module, plan, subscription or permission is seeded.
/// </summary>
[DbContext(typeof(IdentityDbContext))]
[Migration("20261111180000_atomicusagemetering")]
public sealed class AtomicUsageMeteringMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE dbo.TenantUsageBalances
            (
                TenantId uniqueidentifier NOT NULL,
                CapabilityCode nvarchar(150) NOT NULL,
                PeriodStartUtc datetime2(7) NOT NULL,
                PeriodEndUtc datetime2(7) NOT NULL,
                ConsumedUnits bigint NOT NULL CONSTRAINT DF_TenantUsageBalances_Consumed DEFAULT (0),
                CreatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_TenantUsageBalances_Created DEFAULT (SYSUTCDATETIME()),
                UpdatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_TenantUsageBalances_Updated DEFAULT (SYSUTCDATETIME()),
                RowVersion rowversion NOT NULL,
                CONSTRAINT PK_TenantUsageBalances PRIMARY KEY (TenantId, CapabilityCode, PeriodStartUtc),
                CONSTRAINT FK_TenantUsageBalances_Tenants FOREIGN KEY (TenantId) REFERENCES dbo.Tenants(Id),
                CONSTRAINT CK_TenantUsageBalances_Units CHECK (ConsumedUnits >= 0),
                CONSTRAINT CK_TenantUsageBalances_Period CHECK (PeriodEndUtc > PeriodStartUtc),
                CONSTRAINT CK_TenantUsageBalances_Code CHECK (LEN(CapabilityCode) > 0)
            );
            """);

        migrationBuilder.Sql("""
            CREATE TABLE dbo.TenantUsageEvents
            (
                Id uniqueidentifier NOT NULL CONSTRAINT DF_TenantUsageEvents_Id DEFAULT (NEWID()),
                TenantId uniqueidentifier NOT NULL,
                CapabilityCode nvarchar(150) NOT NULL,
                PeriodStartUtc datetime2(7) NOT NULL,
                OperationKey nvarchar(128) NOT NULL,
                EventKind nvarchar(12) NOT NULL,
                Units bigint NOT NULL,
                ActorUserId uniqueidentifier NOT NULL,
                RecordedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_TenantUsageEvents_Recorded DEFAULT (SYSUTCDATETIME()),
                CONSTRAINT PK_TenantUsageEvents PRIMARY KEY (Id),
                CONSTRAINT FK_TenantUsageEvents_Balance FOREIGN KEY
                    (TenantId, CapabilityCode, PeriodStartUtc)
                    REFERENCES dbo.TenantUsageBalances(TenantId, CapabilityCode, PeriodStartUtc),
                CONSTRAINT CK_TenantUsageEvents_Kind CHECK (EventKind IN (N'Consume', N'Reverse')),
                CONSTRAINT CK_TenantUsageEvents_Units CHECK (Units > 0),
                CONSTRAINT CK_TenantUsageEvents_Key CHECK (LEN(OperationKey) >= 8),
                CONSTRAINT UX_TenantUsageEvents_OperationKind UNIQUE
                    (TenantId, CapabilityCode, PeriodStartUtc, OperationKey, EventKind)
            );
            CREATE INDEX IX_TenantUsageEvents_TenantPeriod
                ON dbo.TenantUsageEvents(TenantId, CapabilityCode, PeriodStartUtc, RecordedAtUtc);
            """);

        // Audit events may only be appended. Reversals are new signed events;
        // an UPDATE/DELETE must not erase consumption history.
        var trigger = """
            CREATE OR ALTER TRIGGER dbo.TR_TenantUsageEvents_AppendOnly
            ON dbo.TenantUsageEvents AFTER UPDATE, DELETE AS
            BEGIN
                SET NOCOUNT ON;
                THROW 51101, 'Usage events are append-only; record a reversal.', 1;
            END;
            """;
        migrationBuilder.Sql("EXEC(N'" + trigger.Replace("'", "''") + "')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS dbo.TR_TenantUsageEvents_AppendOnly;");
        migrationBuilder.Sql("DROP TABLE IF EXISTS dbo.TenantUsageEvents;");
        migrationBuilder.Sql("DROP TABLE IF EXISTS dbo.TenantUsageBalances;");
    }
}
