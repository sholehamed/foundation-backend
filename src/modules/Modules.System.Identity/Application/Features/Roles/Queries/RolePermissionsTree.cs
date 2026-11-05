using Modules.System.Identity.Application.Abstractions;

namespace Modules.System.Identity.Application.Features.Roles.Queries
{
    public class RolePermissionsTreeQuery : IQuery<RolePermissionTreeResponseDto>
    {
        public Guid RoleId { get; set; }
    }
    public sealed class RolePermissionTreeResponseDto
    {
        public List<ModulePermissionTreeDto> Modules { get; set; } = new();
        public List<Guid> SelectedPermissionIds { get; set; } = new();
    }

    public sealed class ModulePermissionTreeDto
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = default!;
        public string Title { get; set; } = default!;
        public string? Description { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }

        public List<ResourcePermissionTreeDto> Resources { get; set; } = new();
    }

    public sealed class ResourcePermissionTreeDto
    {
        public Guid Id { get; set; }
        public Guid ModuleId { get; set; }
        public string Code { get; set; } = default!;
        public string Title { get; set; } = default!;
        public string? Description { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }

        public List<PermissionTreeDto> Permissions { get; set; } = new();
    }

    public sealed class PermissionTreeDto
    {
        public Guid Id { get; set; }
        public Guid? ResourceId { get; set; }
        public string Code { get; set; } = default!;
        public string Name { get; set; } = default!;
        public string? Description { get; set; }
        public bool IsActive { get; set; }
    }
    public class RolePermissionTreeQueryHandler : IQueryHandler<RolePermissionsTreeQuery, RolePermissionTreeResponseDto>
    {
        private readonly IIdentityDbContext _context;

        public RolePermissionTreeQueryHandler(IIdentityDbContext context)
        {
            _context = context;
        }

       public async Task<RolePermissionTreeResponseDto> Handle(RolePermissionsTreeQuery request, CancellationToken cancellationToken)
        {
            var selectedPermissionIds = await _context.RolePermissions
             .AsNoTracking()
             .Where(x => x.RoleId == request.RoleId)
             .Select(x => x.PermissionId)
             .ToListAsync(cancellationToken);

            var modules = await _context.Modules
                .AsNoTracking()
                .Where(module => module.IsActive)
                .OrderBy(module => module.SortOrder)
                .ThenBy(module => module.Title)
                .Select(module => new ModulePermissionTreeDto
                {
                    Id = module.Id,
                    Code = module.Code,
                    Title = module.Title,
                    Description = module.Description,
                    SortOrder = module.SortOrder,
                    IsActive = module.IsActive,

                    Resources = module.Resources!
                        .Where(resource => resource.IsActive)
                        .OrderBy(resource => resource.SortOrder)
                        .ThenBy(resource => resource.Title)
                        .Select(resource => new ResourcePermissionTreeDto
                        {
                            Id = resource.Id,
                            ModuleId = resource.ModuleId,
                            Code = resource.Code,
                            Title = resource.Title,
                            Description = resource.Description,
                            SortOrder = resource.SortOrder,
                            IsActive = resource.IsActive,

                            Permissions = resource.Permissions!
                                .Where(permission => permission.IsActive)
                                .OrderBy(permission => permission.Code)
                                .Select(permission => new PermissionTreeDto
                                {
                                    Id = permission.Id,
                                    ResourceId = permission.ResourceId,
                                    Code = permission.Code,
                                    Name = permission.Name,
                                    Description = permission.Description,
                                    IsActive = permission.IsActive
                                })
                                .ToList()
                        })
                        .ToList()
                })
                .ToListAsync(cancellationToken);

            return new RolePermissionTreeResponseDto
            {
                Modules = modules,
                SelectedPermissionIds = selectedPermissionIds
            };
        }
    }
}
