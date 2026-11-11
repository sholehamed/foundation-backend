using System.Data;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;

namespace Infrastructure.SharedKernel.Observability;

/// <summary>
/// Explicit opt-in instrumentation for raw SqlClient commands and source-owned
/// Dapper executions. The caller must own the command/transaction and remain
/// responsible for opening the connection, executing and disposing it.
///
/// EF Core uses a DIFFERENT explicit DbCommandInterceptor and MUST NOT call
/// these wrappers: this prevents one physical EF command creating two spans.
/// </summary>
public static class SourceOwnedSqlTrace
{
    public const string ActivitySourceName = "Foundation.SqlClient";
    public static readonly ActivitySource Source = new(ActivitySourceName);
    private static readonly byte[] FingerprintKey = RandomNumberGenerator.GetBytes(32);
    private const int MaxCommandChars = 32768;

    public static Task<int> ExecuteNonQueryAsync(
        SqlCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return ExecuteAsync(command, "nonquery",
            () => command.ExecuteNonQueryAsync(cancellationToken));
    }

    public static Task<object?> ExecuteScalarAsync(
        SqlCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return ExecuteAsync(command, "scalar",
            () => command.ExecuteScalarAsync(cancellationToken));
    }

    public static Task<SqlDataReader> ExecuteReaderAsync(
        SqlCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return ExecuteAsync(command, "reader",
            () => command.ExecuteReaderAsync(cancellationToken));
    }

    public static Task<SqlDataReader> ExecuteReaderAsync(
        SqlCommand command, CommandBehavior behavior,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return ExecuteAsync(command, "reader",
            () => command.ExecuteReaderAsync(behavior, cancellationToken));
    }

    /// <summary>
    /// Dapper executes through a caller-owned connection. This wrapper times
    /// the actual delegate (including result materialization performed by it).
    /// The source-owned SQL template is used only for a bounded ephemeral
    /// HMAC; values, query text, connection string and result never leave.
    /// </summary>
    public static Task<T> RunDapperAsync<T>(
        string sqlTemplate, Func<Task<T>> execute)
    {
        ArgumentNullException.ThrowIfNull(execute);
        return ExecuteCoreAsync("dapper", "dapper", sqlTemplate,
            parameterCount: null, execute);
    }

    public static Task RunDapperAsync(
        string sqlTemplate, Func<Task> execute)
    {
        ArgumentNullException.ThrowIfNull(execute);
        return ExecuteCoreVoidAsync("dapper", "dapper", sqlTemplate, execute);
    }

    private static Task<T> ExecuteAsync<T>(SqlCommand command,
        string kind, Func<Task<T>> execute)
        => ExecuteCoreAsync("sqlclient", kind, command.CommandText,
            command.Parameters.Count, execute, command.CommandType.ToString());

    private static async Task ExecuteCoreVoidAsync(
        string client, string kind, string? sql, Func<Task> execute)
    {
        // Avoid promoting an unsampled request into a new root and avoid
        // running HMAC when telemetry isn't being captured.
        using var activity = Begin(client, kind, sql, null, "Text");
        try
        {
            await execute();
            activity?.SetTag("foundation.db.outcome", "success");
        }
        catch (Exception error)
        {
            RecordFailure(activity, error);
            throw;
        }
    }

    private static async Task<T> ExecuteCoreAsync<T>(
        string client, string kind, string? sql,
        int? parameterCount, Func<Task<T>> execute, string commandType = "Text")
    {
        using var activity = Begin(client, kind, sql, parameterCount, commandType);
        try
        {
            var result = await execute();
            activity?.SetTag("foundation.db.outcome", "success");
            return result;
        }
        catch (Exception error)
        {
            RecordFailure(activity, error);
            throw;
        }
    }

    private static Activity? Begin(
        string client, string kind, string? sql, int? parameterCount, string commandType)
    {
        if (Activity.Current is not { Recorded: true })
            return null;

        var activity = Source.StartActivity("db.sqlclient.command", ActivityKind.Client);
        if (activity is null) return null;

        activity.SetTag("foundation.db.client", client);
        activity.SetTag("foundation.db.execution", kind);
        activity.SetTag("foundation.db.command_type", commandType);
        if (parameterCount.HasValue)
            activity.SetTag("foundation.db.parameter_count", parameterCount.Value);

        if (sql is { Length: > 0 and <= MaxCommandChars })
        {
            var digest = HMACSHA256.HashData(
                FingerprintKey, Encoding.UTF8.GetBytes(sql));
            activity.SetTag("foundation.db.fingerprint",
                Convert.ToHexStringLower(digest));
            activity.SetTag("foundation.db.fingerprint_scope", "process_sqlclient");
        }
        else
        {
            activity.SetTag("foundation.db.fingerprint_state", "not_collected");
        }
        return activity;
    }

    private static void RecordFailure(Activity? activity, Exception error)
    {
        if (error is OperationCanceledException)
            activity?.SetTag("foundation.db.outcome", "canceled");
        else
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag("foundation.db.outcome", "failed");
        }
        activity?.SetTag("foundation.error.type", error.GetType().Name);
    }
}
