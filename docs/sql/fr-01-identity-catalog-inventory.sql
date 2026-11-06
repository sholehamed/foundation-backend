/*
Foundation / Identity — FR-01 catalog inventory
READ ONLY. No UPDATE/INSERT/DELETE/MERGE/DDL; does not assign or revoke permissions.
Use with a login allowed to SELECT on the Identity tables in the intended database.
Run on a read-only replica or a reviewed maintenance window for large installations.
Confirm DB_NAME() below BEFORE interpreting results. No user names or PII are selected.
Schema assumes dbo, as in the current EF Core model snapshot. For other schemas,
review the script rather than running modified ad-hoc writes.

This is an INVENTORY, not a migration/repair, and must not be used to auto-clean rows.
*/
SET NOCOUNT ON;
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

IF OBJECT_ID(N'dbo.Modules', 'U') IS NULL OR
   OBJECT_ID(N'dbo.Resources', 'U') IS NULL OR
   OBJECT_ID(N'dbo.Permissions', 'U') IS NULL OR
   OBJECT_ID(N'dbo.ApiResources', 'U') IS NULL OR
   OBJECT_ID(N'dbo.UiResources', 'U') IS NULL OR
   OBJECT_ID(N'dbo.Menus', 'U') IS NULL OR
   OBJECT_ID(N'dbo.MenuCategories', 'U') IS NULL OR
   OBJECT_ID(N'dbo.RolePermissions', 'U') IS NULL OR
   OBJECT_ID(N'dbo.UserPermissions', 'U') IS NULL OR
   OBJECT_ID(N'dbo.PermissionApiResources', 'U') IS NULL OR
   OBJECT_ID(N'dbo.PermissionUiResources', 'U') IS NULL OR
   OBJECT_ID(N'dbo.Roles', 'U') IS NULL
BEGIN
    THROW 51040, 'FR-01: Expected Identity dbo tables not found. Check database/schema; no data was changed.', 1;
END;

-- 1) Context and schema feature flags.
SELECT DB_NAME() AS DatabaseName,
       CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)) AS SqlServerVersion,
       CASE WHEN COL_LENGTH('dbo.Permissions', 'NormalizedCode') IS NULL THEN 0 ELSE 1 END
           AS HasNormalizedPermissionCodeColumn,
       CASE WHEN EXISTS
           (SELECT 1 FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'dbo.Permissions')
              AND name = N'UX_Permissions_NormalizedCode')
           THEN 1 ELSE 0 END AS HasNormalizedPermissionCodeUniqueIndex;

-- 2) Entity counts, not user-level data.
SELECT N'Modules' AS EntityName, COUNT_BIG(*) AS Rows FROM dbo.Modules
UNION ALL SELECT N'Resources', COUNT_BIG(*) FROM dbo.Resources
UNION ALL SELECT N'Permissions', COUNT_BIG(*) FROM dbo.Permissions
UNION ALL SELECT N'ApiResources', COUNT_BIG(*) FROM dbo.ApiResources
UNION ALL SELECT N'UiResources', COUNT_BIG(*) FROM dbo.UiResources
UNION ALL SELECT N'MenuCategories', COUNT_BIG(*) FROM dbo.MenuCategories
UNION ALL SELECT N'Menus', COUNT_BIG(*) FROM dbo.Menus
UNION ALL SELECT N'Roles', COUNT_BIG(*) FROM dbo.Roles
UNION ALL SELECT N'RolePermissions', COUNT_BIG(*) FROM dbo.RolePermissions
UNION ALL SELECT N'UserPermissions', COUNT_BIG(*) FROM dbo.UserPermissions
UNION ALL SELECT N'PermissionApiResources', COUNT_BIG(*) FROM dbo.PermissionApiResources
UNION ALL SELECT N'PermissionUiResources', COUNT_BIG(*) FROM dbo.PermissionUiResources
ORDER BY EntityName;

-- 3) Risk counts. None of these results changes or implies a grant.
SELECT N'permissions.invalid_code' AS CheckName, COUNT_BIG(*) AS Rows
FROM dbo.Permissions
WHERE Code IS NULL OR LEN(LTRIM(RTRIM(Code))) NOT BETWEEN 1 AND 100
UNION ALL
SELECT N'permissions.null_resource_id', COUNT_BIG(*) FROM dbo.Permissions WHERE ResourceId IS NULL
UNION ALL
SELECT N'menus.legacy_system_admin_route', COUNT_BIG(*) FROM dbo.Menus
WHERE Route LIKE N'/system/admin/%'
UNION ALL
SELECT N'menus.both_internal_and_external', COUNT_BIG(*) FROM dbo.Menus
WHERE NULLIF(LTRIM(RTRIM(Route)), N'') IS NOT NULL
  AND NULLIF(LTRIM(RTRIM(ExternalUrl)), N'') IS NOT NULL
UNION ALL
SELECT N'menus.missing_permission', COUNT_BIG(*) FROM dbo.Menus m
LEFT JOIN dbo.Permissions p ON p.Id=m.PermissionId
WHERE m.PermissionId IS NOT NULL AND p.Id IS NULL
UNION ALL
SELECT N'menus.missing_parent', COUNT_BIG(*) FROM dbo.Menus m
LEFT JOIN dbo.Menus p ON p.Id=m.ParentId
WHERE m.ParentId IS NOT NULL AND p.Id IS NULL
UNION ALL
SELECT N'menus.cross_category_parent', COUNT_BIG(*) FROM dbo.Menus m
JOIN dbo.Menus p ON p.Id=m.ParentId
WHERE m.CategoryId<>p.CategoryId
UNION ALL
SELECT N'menus.self_parent', COUNT_BIG(*) FROM dbo.Menus
WHERE ParentId=Id
UNION ALL
SELECT N'role_permissions.cross_scope', COUNT_BIG(*) FROM dbo.RolePermissions rp
JOIN dbo.Roles r ON rp.RoleId=r.Id
WHERE r.TenantId IS NULL OR rp.TenantId<>r.TenantId
UNION ALL
SELECT N'permission_api_resources.missing_endpoint', COUNT_BIG(*) FROM dbo.PermissionApiResources link
LEFT JOIN dbo.ApiResources api ON api.Id=link.ApiResourceId
WHERE api.Id IS NULL
UNION ALL
SELECT N'permission_ui_resources.missing_ui', COUNT_BIG(*) FROM dbo.PermissionUiResources link
LEFT JOIN dbo.UiResources ui ON ui.Id=link.UiResourceId
WHERE ui.Id IS NULL
ORDER BY CheckName;

-- 4) Canonical key duplicate detection (comparison matches the reviewed Permission.Code preflight).
SELECT TOP (100) UPPER(LTRIM(RTRIM(Code))) COLLATE Latin1_General_100_CI_AS
           AS NormalizedPermissionCode,
       COUNT_BIG(*) AS DuplicateRows
FROM dbo.Permissions
WHERE Code IS NOT NULL
GROUP BY UPPER(LTRIM(RTRIM(Code))) COLLATE Latin1_General_100_CI_AS
HAVING COUNT_BIG(*) > 1
ORDER BY DuplicateRows DESC, NormalizedPermissionCode;

-- 5) Module / Resource / API / UI key collisions (schema constraints may already block some).
SELECT TOP (100) LOWER(LTRIM(RTRIM(Code))) AS NormalizedModuleCode,
       COUNT_BIG(*) AS DuplicateRows
FROM dbo.Modules
WHERE Code IS NOT NULL
GROUP BY LOWER(LTRIM(RTRIM(Code)))
HAVING COUNT_BIG(*) > 1
ORDER BY DuplicateRows DESC, NormalizedModuleCode;

SELECT TOP (100) ModuleId,
       LOWER(LTRIM(RTRIM(Code))) AS NormalizedResourceCode,
       COUNT_BIG(*) AS DuplicateRows
FROM dbo.Resources
WHERE Code IS NOT NULL
GROUP BY ModuleId, LOWER(LTRIM(RTRIM(Code)))
HAVING COUNT_BIG(*) > 1
ORDER BY DuplicateRows DESC, ModuleId, NormalizedResourceCode;

SELECT TOP (100) UPPER(LTRIM(RTRIM([Key]))) AS NormalizedApiKey,
       COUNT_BIG(*) AS DuplicateRows
FROM dbo.ApiResources
WHERE [Key] IS NOT NULL
GROUP BY UPPER(LTRIM(RTRIM([Key])))
HAVING COUNT_BIG(*) > 1
ORDER BY DuplicateRows DESC, NormalizedApiKey;

SELECT TOP (100) UPPER(LTRIM(RTRIM([Key]))) AS NormalizedUiKey,
       COUNT_BIG(*) AS DuplicateRows
FROM dbo.UiResources
WHERE [Key] IS NOT NULL
GROUP BY UPPER(LTRIM(RTRIM([Key])))
HAVING COUNT_BIG(*) > 1
ORDER BY DuplicateRows DESC, NormalizedUiKey;

-- 6) Endpoint and menu drift candidates. Routes are returned, no users or grants.
SELECT TOP (100) api.[Key], api.HttpMethod, api.RouteTemplate,
       api.IsActive, api.IsDeprecated, api.Source
FROM dbo.ApiResources api
WHERE api.IsDeprecated=1 OR api.IsActive=0
ORDER BY api.[Key];

SELECT TOP (100) m.Id, m.Title, m.Route, m.ExternalUrl
FROM dbo.Menus m
WHERE m.Route LIKE N'/system/admin/%'
   OR (NULLIF(LTRIM(RTRIM(m.Route)), N'') IS NOT NULL
       AND NULLIF(LTRIM(RTRIM(m.ExternalUrl)), N'') IS NOT NULL)
ORDER BY m.Route, m.Id;

-- 7) Permission groups that cannot appear under Module -> Resource in current role tree.
SELECT TOP (100) p.Code, p.Name, p.IsActive,
       (SELECT COUNT_BIG(*) FROM dbo.RolePermissions rp WHERE rp.PermissionId=p.Id) AS RoleGrantRows
FROM dbo.Permissions p
WHERE p.ResourceId IS NULL
ORDER BY p.Code;
