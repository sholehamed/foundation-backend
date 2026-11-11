using System.Diagnostics;
using Microsoft.Data.SqlClient;

namespace Infrastructure.SharedKernel.Observability;

/// <summary>
/// A per-operation, source-owned transaction span. "not_committed" means the
/// application did not confirm CommitAsync: it does NOT assert the database
/// rolled back (commit failures may be indeterminate).
/// </summary>
public sealed class SourceOwnedTransactionTrace : IDisposable
{
    private readonly Activity? _activity;
    private string _outcome = "not_committed";
    private bool _disposed;

    private SourceOwnedTransactionTrace(Activity? activity)
    {
        _activity = activity;
    }

    public static SourceOwnedTransactionTrace Start(string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        if (kind.Length > 64 || kind.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            throw new ArgumentException("Transaction kind must be source-owned metadata", nameof(kind));
        var activity = SourceOwnedSqlTrace.CanCapture()
            ? SourceOwnedSqlTrace.Source.StartActivity("db.sqlclient.transaction", ActivityKind.Internal)
            : null;
        activity?.SetTag("foundation.db.transaction.kind", kind);
        return new SourceOwnedTransactionTrace(activity);
    }

    public void CommitAttempted() => _outcome = "commit_outcome_unknown";
    public void CommitConfirmed() => _outcome = "committed";
    public void RollbackConfirmed() => _outcome = "rolled_back";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _activity?.SetTag("foundation.db.transaction.outcome", _outcome);
        _activity?.Dispose();
    }
}

/// <summary>
/// Explicit async transaction command spans for directly-owned SqlClient
/// transactions, including real successful rollbacks and indeterminate
/// failed commits. Must not wrap EF's internal transaction methods as that
/// would duplicate EF-generated events.
/// </summary>
public static class SourceOwnedSqlTransactionCommands
{
    public static Task CommitAsync(
        SqlTransaction transaction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        return ExecuteAsync("commit", () => transaction.CommitAsync(cancellationToken));
    }

    public static Task RollbackAsync(
        SqlTransaction transaction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        return ExecuteAsync("rollback", () => transaction.RollbackAsync(cancellationToken));
    }

    private static async Task ExecuteAsync(string operation, Func<Task> work)
    {
        using var activity = SourceOwnedSqlTrace.CanCapture()
            ? SourceOwnedSqlTrace.Source.StartActivity(
                "db.sqlclient.transaction." + operation, ActivityKind.Client)
            : null;
        activity?.SetTag("foundation.db.transaction.action", operation);
        try
        {
            await work();
            activity?.SetTag("foundation.db.transaction.outcome",
                operation == "commit" ? "committed" : "rolled_back");
        }
        catch (OperationCanceledException)
        {
            activity?.SetTag("foundation.db.transaction.outcome", "unknown_canceled");
            throw;
        }
        catch (Exception error)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag("foundation.error.type", error.GetType().Name);
            activity?.SetTag("foundation.db.transaction.outcome", "unknown_failure");
            throw;
        }
    }
}
