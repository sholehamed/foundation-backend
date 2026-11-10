/*
FR-17A-1: read-only Identity catalog / legacy entitlement inventory.

Run manually against the **correct approved Identity SQL Server database**;
save sanitized result sets next to the source inventory JSON. Never run with
assumed production credentials, never treat a source-only CI pass as DB proof.

No INSERT, UPDATE, DELETE, MERGE, EXEC, DDL, temp-table writes, or entitlements.
No TenantIds/UserIds/PII are selected. Missing tables/migration: STOP and review
target environment rather than applying corrective SQL.
*/
SET NOCOUNT ON;

-- A. Target and table inventory (metadata only).
SELECT DB_NAME() AS [IdentityDatabase], @@VERSION AS [SqlServerVersion];
SELECT t.name AS [ExpectedTable], CASE WHEN o.object_id IS NOT NULL THEN 'PRESENT' ELSE 'MISSING' END AS [State]
FROM (VALUES
 ('Modules'),('Resources'),('Permissions'),('ApiResources'),('UiResources'),
 ('PermissionApiResources'),('PermissionUiResources'),('MenuCategories'),('Menus'),
 ('Roles'),('RolePermissions'),('UserRoles'),('UserPermissions'),('Tenants'),
 ('TenantModules'),('BusinessProfiles')
) t(name)
LEFT JOIN sys.objects o ON o.object_id = OBJECT_ID(QUOTENAME('dbo') + '.' + QUOTENAME(t.name), 'U')
ORDER BY t.name;

-- B. Catalog headcounts. These are NOT entitlements.
SELECT 'Modules' AS [Entity], COUNT_BIG(*) AS [Rows] FROM dbo.Modules
UNION ALL SELECT 'Resources', COUNT_BIG(*) FROM dbo.Resources
UNION ALL SELECT 'Permissions', COUNT_BIG(*) FROM dbo.Permissions
UNION ALL SELECT 'ApiResources', COUNT_BIG(*) FROM dbo.ApiResources
UNION ALL SELECT 'UiResources', COUNT_BIG(*) FROM dbo.UiResources
UNION ALL SELECT 'PermissionApiResources', COUNT_BIG(*) FROM dbo.PermissionApiResources
UNION ALL SELECT 'PermissionUiResources', COUNT_BIG(*) FROM dbo.PermissionUiResources
UNION ALL SELECT 'Menus', COUNT_BIG(*) FROM dbo.Menus
UNION ALL SELECT 'Roles', COUNT_BIG(*) FROM dbo.Roles
UNION ALL SELECT 'RolePermissions', COUNT_BIG(*) FROM dbo.RolePermissions
UNION ALL SELECT 'TenantModules', COUNT_BIG(*) FROM dbo.TenantModules
UNION ALL SELECT 'BusinessProfiles', COUNT_BIG(*) FROM dbo.BusinessProfiles;

-- C. Resource is approximately a Controller/EndpointGroup. SQL cannot discover
--     C# EndpointGroups: compare this Module/Resource map with JSON source output.
SELECT
 m.Code AS [ModuleCode], m.IsActive AS [ModuleActive],
 r.Code AS [ResourceCode], r.IsActive AS [ResourceActive],
 (SELECT COUNT_BIG(*) FROM dbo.Permissions p WHERE p.ResourceId = r.Id) AS [PermissionCount],
 (SELECT COUNT_BIG(*) FROM dbo.ApiResources a WHERE a.ResourceId = r.Id) AS [ApiEndpointCount],
 (SELECT COUNT_BIG(*) FROM dbo.UiResources u WHERE u.ResourceId = r.Id) AS [UiElementCount]
FROM dbo.Modules m
LEFT JOIN dbo.Resources r ON r.ModuleId = m.Id
ORDER BY m.Code, r.Code;

-- D. Legacy orphan/foreign ownership; no automatic adoption/migration.
SELECT
 p.Code AS [PermissionCode], p.IsActive AS [PermissionActive],
 COALESCE(m.Code, '(none)') AS [ModuleCode],
 COALESCE(r.Code, '(none)') AS [ResourceCode],
 CASE
   WHEN p.ResourceId IS NULL THEN 'UNOWNED_LEGACY'
   WHEN r.Id IS NULL THEN 'BROKEN_RESOURCE_FK'
   WHEN m.Id IS NULL THEN 'BROKEN_MODULE_FK'
   WHEN m.IsActive = 0 OR r.IsActive = 0 THEN 'INACTIVE_OWNER'
   ELSE 'OWNED'
 END AS [OwnerState]
FROM dbo.Permissions p
LEFT JOIN dbo.Resources r ON r.Id = p.ResourceId
LEFT JOIN dbo.Modules m ON m.Id = r.ModuleId
WHERE p.ResourceId IS NULL OR r.Id IS NULL OR m.Id IS NULL
   OR r.IsActive = 0 OR m.IsActive = 0
ORDER BY p.Code;

-- E. Catalog namespace collisions (case-folded). MUST review before Apply.
SELECT UPPER(p.Code) AS [NormalizedPermissionCode], COUNT_BIG(*) AS [Count]
FROM dbo.Permissions p
GROUP BY UPPER(p.Code)
HAVING COUNT_BIG(*) > 1
ORDER BY [NormalizedPermissionCode];

-- F. Cross-Resource links: permission and API/UI must have reviewed owners.
SELECT p.Code AS [PermissionCode], a.[Key] AS [ApiKey],
       pm.Code AS [PermissionModule], pr.Code AS [PermissionResource],
       am.Code AS [ApiModule], ar.Code AS [ApiResource]
FROM dbo.PermissionApiResources x
JOIN dbo.Permissions p ON p.Id = x.PermissionId
JOIN dbo.ApiResources a ON a.Id = x.ApiResourceId
LEFT JOIN dbo.Resources pr ON pr.Id = p.ResourceId
LEFT JOIN dbo.Modules pm ON pm.Id = pr.ModuleId
LEFT JOIN dbo.Resources ar ON ar.Id = a.ResourceId
LEFT JOIN dbo.Modules am ON am.Id = ar.ModuleId
WHERE p.ResourceId IS NULL OR a.ResourceId <> p.ResourceId OR pr.Id IS NULL OR ar.Id IS NULL
ORDER BY p.Code, a.[Key];

SELECT p.Code AS [PermissionCode], u.[Key] AS [UiKey],
       pm.Code AS [PermissionModule], pr.Code AS [PermissionResource],
       um.Code AS [UiModule], ur.Code AS [UiResource]
FROM dbo.PermissionUiResources x
JOIN dbo.Permissions p ON p.Id = x.PermissionId
JOIN dbo.UiResources u ON u.Id = x.UiResourceId
LEFT JOIN dbo.Resources pr ON pr.Id = p.ResourceId
LEFT JOIN dbo.Modules pm ON pm.Id = pr.ModuleId
LEFT JOIN dbo.Resources ur ON ur.Id = u.ResourceId
LEFT JOIN dbo.Modules um ON um.Id = ur.ModuleId
WHERE p.ResourceId IS NULL OR u.ResourceId <> p.ResourceId OR pr.Id IS NULL OR ur.Id IS NULL
ORDER BY p.Code, u.[Key];

-- G. Exactly reviewed System route/permission binding, no personal data.
SELECT
 c.Title AS [MenuCategory], c.IsActive AS [CategoryActive],
 n.Title AS [MenuTitle], n.Route,
 n.IsActive AS [MenuActive], n.IsHidden,
 COALESCE(p.Code,'(unbound)') AS [PermissionCode],
 COALESCE(r.Code,'(unbound)') AS [ResourceCode],
 COALESCE(m.Code,'(unbound)') AS [ModuleCode]
FROM dbo.Menus n
LEFT JOIN dbo.MenuCategories c ON c.Id = n.CategoryId
LEFT JOIN dbo.Permissions p ON p.Id = n.PermissionId
LEFT JOIN dbo.Resources r ON r.Id = p.ResourceId
LEFT JOIN dbo.Modules m ON m.Id = r.ModuleId
WHERE n.Route LIKE '/system/%' OR n.Route LIKE '/dashboard%'
ORDER BY n.Route;

-- H. Legacy TenantModules: an IsEnabled flag is NOT a subscription.
SELECT m.Code AS [ModuleCode],
       COUNT_BIG(*) AS [TenantModuleRows],
       SUM(CASE WHEN tm.IsEnabled = 1 THEN 1 ELSE 0 END) AS [EnabledRows],
       SUM(CASE WHEN tm.IsTrial = 1 THEN 1 ELSE 0 END) AS [TrialRows],
       SUM(CASE WHEN tm.CustomExpiryDate < SYSUTCDATETIME() THEN 1 ELSE 0 END) AS [ExpiredByRawDateRows]
FROM dbo.TenantModules tm
LEFT JOIN dbo.Modules m ON m.Id = tm.ModuleId
GROUP BY m.Code
ORDER BY m.Code;

-- I. Signup intent is NOT an active Plan/Subscription: no Tenant identifiers.
SELECT NULLIF(RequestedPlanKey, '') AS [RequestedPlanKey],
       NULLIF(RequestedBillingCycle, '') AS [RequestedBillingCycle],
       COUNT_BIG(*) AS [BusinessProfiles]
FROM dbo.BusinessProfiles
GROUP BY NULLIF(RequestedPlanKey, ''), NULLIF(RequestedBillingCycle, '')
ORDER BY [BusinessProfiles] DESC, [RequestedPlanKey];
