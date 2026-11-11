using Infrastructure.SharedKernel.Observability;
using System.Data;
using Microsoft.Data.SqlClient;

namespace Server.Bff;

/// <summary>
/// Explicit, bounded SQL session-table maintenance. Not scheduled at request
/// time and never granted DDL permissions. Every batch uses SQL Server UTC.
/// Renew is UPDATE-only, so deletion of aged rows cannot re-create a session.
/// </summary>
public sealed class SqlServerBffSessionCleanup(string connectionString)
{
    // The last possible live ticket expires <= AbsoluteExpiresUtc.
    // Extra retention preserves revoked/expired rows during delayed diagnostics,
    // clock disagreement and incident response. Operators may retain longer.
    public const int RetentionHoursAfterAbsoluteExpiry = 48;
    public const int MaximumBatchSize = 1000;

    /// <summary>
    /// Delete at most batchSize fully aged rows, regardless of revoke state.
    /// Run with a separate least-privilege maintenance login granted DELETE;
    /// the API runtime login needs only SELECT/INSERT/UPDATE.
    /// </summary>
    public async Task<int> DeleteExpiredBatchAsync(
        int batchSize, CancellationToken cancellationToken = default)
    {
        if (batchSize is < 1 or > MaximumBatchSize)
            throw new ArgumentOutOfRangeException(
                nameof(batchSize), batchSize,
                $"Batch size must be 1..{MaximumBatchSize}.");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            DELETE TOP (@batchSize) FROM dbo.FoundationBffSessions
                WITH (ROWLOCK, READPAST, READCOMMITTEDLOCK)
            WHERE AbsoluteExpiresUtc < DATEADD(
                    HOUR, -48, SYSUTCDATETIME())
                AND ExpiresUtc < SYSUTCDATETIME();
            """, connection);
        command.Parameters.Add("@batchSize", SqlDbType.Int).Value = batchSize;
        command.CommandTimeout = 30;
        return await SourceOwnedSqlTrace.ExecuteNonQueryAsync(command, cancellationToken);
    }
}
