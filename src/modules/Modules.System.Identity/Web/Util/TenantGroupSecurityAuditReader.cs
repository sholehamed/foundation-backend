using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Modules.System.Identity.Infrastructure.Persistence.Contexts;

namespace Modules.System.Identity.Web.Util;

/// <summary>
/// Owner-only endpoint consumer; SQL itself always binds BOTH TenantId and GroupId.
/// Unattributed links are informational: old valid grants predate the ledgers.
/// No mutation, repair, bulk read or secret fields are exposed.
/// </summary>
public static class TenantGroupSecurityAuditReader
{
    public const int PageSize = 20;
    public sealed record Entry(
        Guid Id, string Category, string Action, DateTime OccurredAtUtc,
        Guid ActorUserId, string? ActorUserName,
        Guid? TargetUserId, string? TargetUserName,
        Guid? RoleId, string? RoleName,
        string? PreviousName, string? CurrentName,
        bool? PreviousActive, bool? CurrentActive);

    public sealed record Integrity(
        int MemberLinks, int RoleLinks, int OwnerMemberLinks,
        int InvalidMemberLinks, int InvalidRoleLinks,
        int UnattributedMemberLinks, int UnattributedRoleLinks);

    public sealed record Page(
        Guid GroupId, string GroupName, IReadOnlyList<Entry> Items,
        int PageNumber, bool HasNext, Integrity Integrity);

    public static async Task<Page> ReadAsync(
        IdentityDbContext db, Guid tenantId, Guid groupId, string groupName,
        int page, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty || groupId == Guid.Empty ||
            page is < 1 or > 1000 || !db.Database.IsSqlServer())
            throw new ArgumentException("Invalid Tenant group audit query.");

        // All three append-only ledgers explicitly filter by both identifiers.
        // Joins to mutable user names additionally restrict TenantId. A deleted
        // actor/target keeps their stable GUID without leaking foreign user data.
        const string timelineSql = """
            WITH [events] AS
            (
                SELECT [Id], [ActorUserId], [OccurredAtUtc], [Action],
                    CAST(N'group' AS nvarchar(24)) AS [Category],
                    CAST(NULL AS uniqueidentifier) AS [TargetUserId],
                    CAST(NULL AS uniqueidentifier) AS [RoleId],
                    CAST(NULL AS nvarchar(100)) AS [RoleName],
                    [PreviousName], [CurrentName], [PreviousActive], [CurrentActive]
                FROM [dbo].[TenantGroupChangeAuditEntries]
                WHERE [TenantId] = @tenantId AND [GroupId] = @groupId
                UNION ALL
                SELECT [Id], [ActorUserId], [OccurredAtUtc], [Action],
                    CAST(N'group_member' AS nvarchar(24)) AS [Category],
                    [TargetUserId], CAST(NULL AS uniqueidentifier),
                    CAST(NULL AS nvarchar(100)), CAST(NULL AS nvarchar(100)),
                    CAST(NULL AS nvarchar(100)), CAST(NULL AS bit), CAST(NULL AS bit)
                FROM [dbo].[TenantGroupMembershipAuditEntries]
                WHERE [TenantId] = @tenantId AND [GroupId] = @groupId
                UNION ALL
                SELECT [Id], [ActorUserId], [OccurredAtUtc], [Action],
                    CAST(N'group_role' AS nvarchar(24)) AS [Category],
                    CAST(NULL AS uniqueidentifier), [RoleId], [RoleName],
                    CAST(NULL AS nvarchar(100)), CAST(NULL AS nvarchar(100)),
                    CAST(NULL AS bit), CAST(NULL AS bit)
                FROM [dbo].[TenantGroupRoleAuditEntries]
                WHERE [TenantId] = @tenantId AND [GroupId] = @groupId
            )
            SELECT e.[Id], e.[Category], e.[Action], e.[OccurredAtUtc],
                e.[ActorUserId], actor.[UserName],
                e.[TargetUserId], target.[UserName],
                e.[RoleId], e.[RoleName], e.[PreviousName], e.[CurrentName],
                e.[PreviousActive], e.[CurrentActive]
            FROM [events] e
            LEFT JOIN [dbo].[Users] actor
              ON actor.[Id] = e.[ActorUserId] AND actor.[TenantId] = @tenantId
            LEFT JOIN [dbo].[Users] target
              ON target.[Id] = e.[TargetUserId] AND target.[TenantId] = @tenantId
            ORDER BY e.[OccurredAtUtc] DESC, e.[Id] DESC
            OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;
            """;

        // Read-only assessment of retained (including historical) links. An
        // absent audit "grant" can mean legitimate pre-ledger state: call it
        // "unattributed", NEVER a proven unauthorized privilege.
        // Link foreign keys may be globally valid while TenantIds disagree.
        const string integritySql = """
            SELECT
              (SELECT COUNT(*) FROM [dbo].[UserGroups] ug
               WHERE ug.[TenantId] = @tenantId AND ug.[GroupId] = @groupId),
              (SELECT COUNT(*) FROM [dbo].[GroupRoles] gr
               WHERE gr.[TenantId] = @tenantId AND gr.[GroupId] = @groupId),
              (SELECT COUNT(*) FROM [dbo].[UserGroups] ug
               WHERE ug.[TenantId] = @tenantId AND ug.[GroupId] = @groupId
                 AND EXISTS
                   (SELECT 1 FROM [dbo].[UserRoles] ur
                    INNER JOIN [dbo].[Roles] ownerRole
                      ON ownerRole.[Id] = ur.[RoleId] AND ownerRole.[TenantId] = @tenantId
                    WHERE ur.[TenantId] = @tenantId
                      AND ur.[UserId] = ug.[UserId]
                      AND ownerRole.[NormalizedName] = N'OWNER')),
              (SELECT COUNT(*) FROM [dbo].[UserGroups] ug
               LEFT JOIN [dbo].[Users] u
                 ON u.[Id] = ug.[UserId] AND u.[TenantId] = @tenantId
               WHERE ug.[TenantId] = @tenantId AND ug.[GroupId] = @groupId
                 AND (u.[Id] IS NULL OR u.[UserType] <> 2)),
              (SELECT COUNT(*) FROM [dbo].[GroupRoles] gr
               LEFT JOIN [dbo].[Roles] r
                 ON r.[Id] = gr.[RoleId] AND r.[TenantId] = @tenantId
               WHERE gr.[TenantId] = @tenantId AND gr.[GroupId] = @groupId
                 AND (r.[Id] IS NULL OR r.[NormalizedName] = N'OWNER'
                   OR r.[Scope] <> 2)),
              (SELECT COUNT(*) FROM [dbo].[UserGroups] ug
               WHERE ug.[TenantId] = @tenantId AND ug.[GroupId] = @groupId
                 AND NOT EXISTS
                   (SELECT 1 FROM [dbo].[TenantGroupMembershipAuditEntries] audit
                    WHERE audit.[TenantId] = ug.[TenantId]
                      AND audit.[GroupId] = ug.[GroupId]
                      AND audit.[TargetUserId] = ug.[UserId]
                      AND audit.[Action] = N'grant')),
              (SELECT COUNT(*) FROM [dbo].[GroupRoles] gr
               WHERE gr.[TenantId] = @tenantId AND gr.[GroupId] = @groupId
                 AND NOT EXISTS
                   (SELECT 1 FROM [dbo].[TenantGroupRoleAuditEntries] audit
                    WHERE audit.[TenantId] = gr.[TenantId]
                      AND audit.[GroupId] = gr.[GroupId]
                      AND audit.[RoleId] = gr.[RoleId]
                      AND audit.[Action] = N'grant'));
            """;

        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(ct);
        try
        {
            await using var timeline = connection.CreateCommand();
            timeline.CommandText = timelineSql;
            timeline.Parameters.Add(new SqlParameter("@tenantId", SqlDbType.UniqueIdentifier)
                { Value = tenantId });
            timeline.Parameters.Add(new SqlParameter("@groupId", SqlDbType.UniqueIdentifier)
                { Value = groupId });
            timeline.Parameters.Add(new SqlParameter("@skip", SqlDbType.Int)
                { Value = (page - 1) * PageSize });
            timeline.Parameters.Add(new SqlParameter("@take", SqlDbType.Int)
                { Value = PageSize + 1 });

            var rows = new List<Entry>(PageSize + 1);
            await using (var reader = await timeline.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    rows.Add(new Entry(
                        reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                        DateTime.SpecifyKind(reader.GetDateTime(3), DateTimeKind.Utc),
                        reader.GetGuid(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                        reader.IsDBNull(6) ? null : reader.GetGuid(6),
                        reader.IsDBNull(7) ? null : reader.GetString(7),
                        reader.IsDBNull(8) ? null : reader.GetGuid(8),
                        reader.IsDBNull(9) ? null : reader.GetString(9),
                        reader.IsDBNull(10) ? null : reader.GetString(10),
                        reader.IsDBNull(11) ? null : reader.GetString(11),
                        reader.IsDBNull(12) ? null : reader.GetBoolean(12),
                        reader.IsDBNull(13) ? null : reader.GetBoolean(13)));
                }
            }

            await using var diagnostics = connection.CreateCommand();
            diagnostics.CommandText = integritySql;
            diagnostics.Parameters.Add(new SqlParameter("@tenantId", SqlDbType.UniqueIdentifier)
                { Value = tenantId });
            diagnostics.Parameters.Add(new SqlParameter("@groupId", SqlDbType.UniqueIdentifier)
                { Value = groupId });
            await using var stats = await diagnostics.ExecuteReaderAsync(ct);
            if (!await stats.ReadAsync(ct))
                throw new InvalidOperationException("Group integrity query returned no result.");
            var snapshot = new Integrity(
                stats.GetInt32(0), stats.GetInt32(1), stats.GetInt32(2),
                stats.GetInt32(3), stats.GetInt32(4), stats.GetInt32(5),
                stats.GetInt32(6));
            return new Page(groupId, groupName, rows.Take(PageSize).ToArray(),
                page, rows.Count > PageSize, snapshot);
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }
}
