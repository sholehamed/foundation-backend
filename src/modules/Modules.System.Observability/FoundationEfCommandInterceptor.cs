using System.Data.Common;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Modules.System.Observability;

/// <summary>
/// Records one completed EF Core relational command as a child Activity of the
/// current CQRS/HTTP trace, using EF's observed StartTime + Duration.
///
/// Standard mode DOES NOT record SQL text, literals, parameters, connection
/// strings, user data, exception messages or raw query plans. Fingerprints are
/// HMAC digests keyed by a random per-process key and cannot be compared across
/// process restarts. This is intentional until reviewed durable key rotation.
/// No query behavior, connection, result, or transaction is modified.
/// </summary>
public sealed class FoundationEfCommandInterceptor : DbCommandInterceptor
{
    private static readonly byte[] EphemeralFingerprintKey = RandomNumberGenerator.GetBytes(32);
    private const int MaximumSqlBytesForFingerprint = 65536;

    public override DbDataReader ReaderExecuted(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        Record(command, eventData, "reader", false, null);
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        Record(command, eventData, "reader", false, null);
        return new ValueTask<DbDataReader>(result);
    }

    public override int NonQueryExecuted(
        DbCommand command, CommandExecutedEventData eventData, int result)
    {
        Record(command, eventData, "nonquery", false, null);
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        Record(command, eventData, "nonquery", false, null);
        return new ValueTask<int>(result);
    }

    public override object? ScalarExecuted(
        DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        Record(command, eventData, "scalar", false, null);
        return result;
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, object? result,
        CancellationToken cancellationToken = default)
    {
        Record(command, eventData, "scalar", false, null);
        return new ValueTask<object?>(result);
    }

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData)
    {
        Record(command, eventData, "failure", true, eventData.Exception);
    }

    public override Task CommandFailedAsync(
        DbCommand command, CommandErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Record(command, eventData, "failure", true, eventData.Exception);
        return Task.CompletedTask;
    }

    private static void Record(
        DbCommand command, CommandEndEventData eventData,
        string executionKind, bool failed, Exception? exception)
    {
        // No database operation is changed when telemetry is disabled.
        // Never create a new root Activity for a standalone SQL command in
        // FR-18D1: parent scope is required to avoid unscoped sensitive data.
        if (Activity.Current is null || !Activity.Current.Recorded)
            return;

        using var activity = FoundationTelemetry.ActivitySource.StartActivity(
            "db.ef.command", ActivityKind.Client);
        if (activity is null) return;

        // EF measures execution only (not full reader enumeration). Use the
        // authoritative EF timestamp and elapsed time for a correct Waterfall.
        // The span is emitted on completion to avoid shared command-state maps
        // and any extra latency/locks in actual SQL execution.
        activity.SetStartTime(eventData.StartTime.UtcDateTime);
        activity.SetEndTime(eventData.StartTime.UtcDateTime + eventData.Duration);
        activity.SetTag("foundation.db.client", "ef-core");
        activity.SetTag("foundation.db.execution", executionKind);
        activity.SetTag("foundation.db.command_source", eventData.CommandSource.ToString());
        activity.SetTag("foundation.db.context_type", eventData.Context?.GetType().Name ?? "unknown");
        activity.SetTag("foundation.db.command_type", command.CommandType.ToString());
        activity.SetTag("foundation.db.parameter_count", command.Parameters.Count);

        var sql = command.CommandText;
        // Prevent accidental large allocations and unknown SQL contents in
        // exporter. Only a one-way HMAC of bounded text is ever emitted.
        if (sql is not null && sql.Length <= MaximumSqlBytesForFingerprint)
        {
            var digest = HMACSHA256.HashData(
                EphemeralFingerprintKey, Encoding.UTF8.GetBytes(sql));
            activity.SetTag("foundation.db.fingerprint", Convert.ToHexString(digest).ToLowerInvariant());
            activity.SetTag("foundation.db.fingerprint_scope", "process");
        }
        else
        {
            activity.SetTag("foundation.db.fingerprint_state", "omitted_over_budget");
        }

        if (failed)
        {
            activity.SetStatus(ActivityStatusCode.Error);
            if (exception is not null)
                activity.SetTag("foundation.error.type", exception.GetType().Name);
        }
    }
}
