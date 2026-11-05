using Modules.System.Identity.Application.Abstractions;

namespace Modules.System.Identity.Application.Features.Dashboard
{
    public class SuperDashboardQuery : IQuery<SuperDashboardDto>
    {
    }
    public record SuperDashboardDto
    {
        public int TotalTenants { get; set; }
        public int ActiveTenants { get; set; }
        public int TotalUsers { get; set; }
        public int TotalRoles { get; set; }
        public int TotalPermissions { get; set; }
    }
    public class SuperDashboardQueryHandler : IQueryHandler<SuperDashboardQuery, SuperDashboardDto>
    {
        private readonly IIdentityDbContext _context;

        public SuperDashboardQueryHandler(IIdentityDbContext context)
        {
            _context = context;
        }

        public async Task<SuperDashboardDto> Handle(SuperDashboardQuery request, CancellationToken cancellationToken)
        {
            SuperDashboardDto result = new SuperDashboardDto();
            var tenants = _context.Tenants.AsNoTracking();
            var roles = _context.Roles.AsNoTracking();
            var users = _context.Users.AsNoTracking();
            var permissions = _context.Permissions.AsNoTracking();
            result.TotalTenants = await tenants.Select(x => 1).CountAsync();
            result.ActiveTenants = await tenants.Where(x => x.IsActive).Select(x => 1).CountAsync();
            result.TotalUsers = await users.Select(x => 1).CountAsync();
            result.TotalRoles = await roles.Select(x => 1).CountAsync();
            result.TotalPermissions = await permissions.Select(x => 1).CountAsync();
            return result;

        }
    }
}
