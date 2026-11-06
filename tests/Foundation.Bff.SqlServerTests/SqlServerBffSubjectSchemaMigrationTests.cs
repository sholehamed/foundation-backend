using System.Data;
using Microsoft.Data.SqlClient;
using Server.Bff;
using Xunit;

namespace Foundation.Bff.SqlServerTests;

public sealed class SqlServerBffSubjectSchemaMigrationTests
{
    [Fact]
    public async Task Upgrading_legacy_schema_revokes_unbound_active_rows_and_restores_trusted_schema()
        => await SqlServerBffAtomicStoreTests.WithDatabaseAsync(async cs =>
        {
            await using var connection = new SqlConnection(cs);
            await connection.OpenAsync();

            // Simulate previous v1 deployment with no subject ownership and a
            // previously issued ticket (never trust it after schema upgrade).
            await using (var prior = new SqlCommand("""
                DROP INDEX IX_FoundationBffSessions_Subject_Active
                    ON dbo.FoundationBffSessions;
                ALTER TABLE dbo.FoundationBffSessions
                    DROP CONSTRAINT CK_FoundationBffSessions_Subject;
                ALTER TABLE dbo.FoundationBffSessions DROP COLUMN SubjectId;

                INSERT INTO dbo.FoundationBffSessions
                    (SessionKey, TicketCiphertext, CreatedUtc, ExpiresUtc, AbsoluteExpiresUtc)
                VALUES (@key, 0x01020304, DATEADD(HOUR, -1, SYSUTCDATETIME()),
                    DATEADD(HOUR, 1, SYSUTCDATETIME()),
                    DATEADD(HOUR, 8, SYSUTCDATETIME()));
                """, connection))
            {
                prior.Parameters.Add("@key", SqlDbType.VarChar, 80).Value =
                    "foundation:bff:session:v1:" + new string('A', 43);
                await prior.ExecuteNonQueryAsync();
            }

            using var resource = typeof(SqlServerBffAtomicTicketStore).Assembly
                .GetManifestResourceStream("Foundation.Bff.SqlServerSchema.sql")
                ?? throw new InvalidOperationException("Embedded BFF migration missing.");
            using var stream = new StreamReader(resource);
            var migrationText = await stream.ReadToEndAsync();

            await using (var migration = new SqlCommand(migrationText, connection))
                await migration.ExecuteNonQueryAsync();

            // A second deploy should remain idempotent.
            await using (var second = new SqlCommand(migrationText, connection))
                await second.ExecuteNonQueryAsync();

            await using var check = new SqlCommand("""
                SELECT COUNT(*) FROM dbo.FoundationBffSessions
                WHERE RevokedUtc IS NOT NULL AND TicketCiphertext IS NULL
                    AND SubjectId IS NULL;
                """, connection);
            Assert.Equal(1, Convert.ToInt32(await check.ExecuteScalarAsync()));

            // The updated startup guard rejects untrusted or absent subject
            // metadata and accepts the upgraded, trusted table.
            await new SqlServerBffSchemaStartupGuard(cs).StartAsync(
                CancellationToken.None);
        });

    [Fact]
    public async Task Subject_check_guard_rejects_untrusted_owner_binding()
        => await SqlServerBffAtomicStoreTests.WithDatabaseAsync(async cs =>
        {
            var guard = new SqlServerBffSchemaStartupGuard(cs);
            await guard.StartAsync(CancellationToken.None);
            await using var connection = new SqlConnection(cs);
            await connection.OpenAsync();
            await using (var alter = new SqlCommand("""
                ALTER TABLE dbo.FoundationBffSessions
                    NOCHECK CONSTRAINT CK_FoundationBffSessions_Subject;
                """, connection))
                await alter.ExecuteNonQueryAsync();

            var failure = await Assert.ThrowsAsync<InvalidOperationException>(
                () => guard.StartAsync(CancellationToken.None));
            Assert.Contains("schema/constraints", failure.Message);
        });
}
