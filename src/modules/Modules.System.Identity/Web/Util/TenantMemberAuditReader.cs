using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

namespace Modules.System.Identity.Web.Util;

/// <summary>
/// Tenant-local, bounded read model for the existing append-only SQL ledgers.
/// No secret hashes, invitation tokens, passwords, or caller-supplied TenantId.
/// The endpoint separately enforces live local Owner + MEMBERS.READ authority.
/// </summary>
public static class TenantMemberAuditReader
{
    public const int PageSize = 20;

    public sealed record Entry(
        Guid Id, string Category, string Action, DateTime OccurredAtUtc,
        Guid ActorUserId, string? ActorUserName,
        Guid TargetUserId, string? TargetUserName, string? RoleName);

    public sealed record Page(IReadOnlyList<Entry> Items, int PageNumber, bool HasNext);

    public static async Task<Page> ReadAsync(
        IdentityDbContext db, Guid tenantId, int page, Guid? memberId,
        CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty || page is < 1 or > 1000 ||
            memberId == Guid.Empty || !db.Database.IsSqlServer())
            throw new ArgumentException("Invalid Tenant member audit query.");

        // Explicit TenantId on BOTH ledgers and BOTH user joins. These tables
        // intentionally have no EF query filter and retain records for deleted
        // users; never rely on a caller-controlled ID or the ambient filter.
        const string sql = """
            WITH [events] AS
            (
                SELECT [Id], [ActorUserId], [TargetUserId], [Action],
                    [OccurredAtUtc],
                    CAST(N'member' AS nvarchar(16)) AS [Category],
                    CAST(NULL AS nvarchar(100)) AS [RoleName]
                FROM [dbo].[TenantMemberLifecycleAuditEntries]
                WHERE [TenantId] = @tenantId
                  AND (@memberId IS NULL OR [TargetUserId] = @memberId)
                UNION ALL
                SELECT [Id], [ActorUserId], [TargetUserId], [Action],
                    [OccurredAtUtc],
                    CAST(N'member_role' AS nvarchar(16)) AS [Category],
                    [RoleName]
                FROM [dbo].[TenantMemberRoleAuditEntries]
                WHERE [TenantId] = @tenantId
                  AND (@memberId IS NULL OR [TargetUserId] = @memberId)
            )
            SELECT e.[Id], e.[Category], e.[Action], e.[OccurredAtUtc],
                e.[ActorUserId], actor.[UserName],
                e.[TargetUserId], target.[UserName], e.[RoleName]
            FROM [events] e
            LEFT JOIN [dbo].[Users] actor
              ON actor.[Id] = e.[ActorUserId] AND actor.[TenantId] = @tenantId
            LEFT JOIN [dbo].[Users] target
              ON target.[Id] = e.[TargetUserId] AND target.[TenantId] = @tenantId
            ORDER BY e.[OccurredAtUtc] DESC, e.[Id] DESC
            OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;
            """;

        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.Add(new SqlParameter("@tenantId", SqlDbType.UniqueIdentifier)
                { Value = tenantId });
            command.Parameters.Add(new SqlParameter("@memberId", SqlDbType.UniqueIdentifier)
                { Value = (object?)memberId ?? DBNull.Value });
            command.Parameters.Add(new SqlParameter("@skip", SqlDbType.Int)
                { Value = (page - 1) * PageSize });
            command.Parameters.Add(new SqlParameter("@take", SqlDbType.Int)
                { Value = PageSize + 1 });

            var rows = new List<Entry>(PageSize + 1);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                rows.Add(new Entry(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc),
                    reader.GetGuid(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.GetGuid(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8)));
            }
            return new Page(rows.Take(PageSize).ToArray(), page, rows.Count > PageSize);
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }
}
