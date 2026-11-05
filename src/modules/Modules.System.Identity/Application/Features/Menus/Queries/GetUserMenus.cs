using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Menus.Queries
{
    public  record GetUserMenusQuery(Guid RoleId):IQuery<List<MenuCategoryDto>>
    {
    }
    public class MenuCategoryDto
    {
        public string Title { get; set; } = default!;
        public List<MenuDto> Items { get; set; } = [];
    }
    public class MenuDto
    {
        public string? Route { get; set; }
        public string Title { get; set; } = default!;
        public string Icon { get; set; } = default!;
        public List<MenuDto> Childs { get; set; } = [];

    }
    public class GetUserMenusQueryHandler(IIdentityDbContext _context,ICurrentUser auth) : IQueryHandler<GetUserMenusQuery, List<MenuCategoryDto>>
    {

        private async Task<List<MenuCategoryDto>> GetMenusAsync(
     Guid roleId,
     Guid userId,
     CancellationToken cancellationToken = default)
        {
            // 1) دسترسی‌های Role
            var rolePermissionIds = await _context.RolePermissions
                .Where(rp => rp.RoleId == roleId)
                .Select(rp => rp.PermissionId)
                .ToListAsync(cancellationToken);

            // 2) دسترسی‌های مستقیم کاربر
            var userPermissions = await _context.UserPermissions
                .Where(up => up.UserId == userId)
                .Select(up => new { up.PermissionId, up.IsGranted })
                .ToListAsync(cancellationToken);

            var userGranted = userPermissions
                .Where(up => up.IsGranted)
                .Select(up => up.PermissionId);

            var userDenied = userPermissions
                .Where(up => !up.IsGranted)
                .Select(up => up.PermissionId)
                .ToHashSet();

            // 3) دسترسی مؤثر
            var effectivePermissionIds = rolePermissionIds
                .Concat(userGranted)
                .Where(id => !userDenied.Contains(id))
                .ToHashSet();

            // 4) همه منوهای فعال رو بکش بیرون (برای ساخت درخت در حافظه)
            var allMenus = await _context.Menus.AsNoTracking().OrderBy(x=>x.Order)
                .Where(m => m.IsActive && !m.IsHidden)
                .ToListAsync(cancellationToken);

            // 5) فیلتر بر اساس دسترسی
            var accessibleMenus = allMenus
                .Where(m => m.PermissionId == null || effectivePermissionIds.Contains(m.PermissionId.Value))
                .ToList();

            // 6) فقط منوهایی که والدشون هم accessible هست (یا خودشون root هستن)
            var accessibleIds = accessibleMenus.Select(m => m.Id).ToHashSet();
            var validMenus = accessibleMenus
                .Where(m => m.ParentId == null || accessibleIds.Contains(m.ParentId.Value))
                .ToList();

            // 7) گروه‌بندی بر اساس دسته
            var categoriesWithMenus = validMenus
                .GroupBy(m => m.CategoryId)
                .Select(g => new
                {
                    CategoryId = g.Key,
                    Menus = g.ToList()
                })
                .ToList();

            // 8) دریافت اطلاعات دسته‌ها
            var categoryIds = categoriesWithMenus.Select(c => c.CategoryId).ToList();
            var categories = await _context.MenuCategories.AsNoTracking().OrderBy(x=>x.Order)
                .Where(c => c.IsActive && categoryIds.Contains(c.Id))
                .ToListAsync(cancellationToken);

            // 9) ساخت خروجی نهایی با ساختار درختی
            var result = categories.Select(cat =>
            {
                var categoryMenus = categoriesWithMenus
                    .First(cm => cm.CategoryId == cat.Id)
                    .Menus;

                var menuTree = BuildMenuTree(categoryMenus, null);

                return new MenuCategoryDto
                {
                    Title = cat.Title,
                    Items = menuTree
                };
            })
            .Where(c => c.Items.Count>0)
            .ToList();

            return result;
        }

        public async Task<List<MenuCategoryDto>> Handle(GetUserMenusQuery request, CancellationToken cancellationToken)
        {
            Guid userId = auth.UserId;
            var res = await GetMenusAsync(request.RoleId, userId,cancellationToken);
            return res;
        }
        private static List<MenuDto> BuildMenuTree(List<Menu> allMenus, Guid? parentId)
        {
            return [.. allMenus
                .Where(m => m.ParentId == parentId)
                .Select(m => new MenuDto
                {
                    Title = m.Title,
                    Icon = m.Icon!,
                    Route=m.Route,
                    Childs = BuildMenuTree(allMenus, m.Id)
                })];
        }
    }
}
