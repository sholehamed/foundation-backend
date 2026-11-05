using Modules.System.Identity.Application.Abstractions;

namespace Modules.System.Identity.Application.Features.Permissions.Queries
{
    public record PermissionLookupQuery(string? text) : IQuery<List<PermissionlookupDto>>;

    public record PermissionlookupDto
    {
        public Guid Id { get; init; }
        public string Code { get; init; }
        public string Name { get; init; }
        public string Category { get; init; }
    }
    public class PermissionLookupQueryHandler : IQueryHandler<PermissionLookupQuery, List<PermissionlookupDto>>
    {
        private readonly IIdentityDbContext _context;

        public PermissionLookupQueryHandler(IIdentityDbContext context)
        {
            _context = context;
        }

        public async Task<List<PermissionlookupDto>> Handle(PermissionLookupQuery request, CancellationToken ct)
        {
            var permissions = _context.Permissions.Where(p => p.IsActive);
            if (!string.IsNullOrEmpty(request.text))
                permissions = permissions.Where(x => (!string.IsNullOrEmpty(request.text) && x.Code.Contains(request.text)  || x.Name.Contains(request.text)));
            var list = await permissions.Take(20)
             .Select(p => new PermissionlookupDto
             {
                 Id = p.Id,
                 Code = p.Code,
                 Name = p.Name,
             })
            
             .ToListAsync();
            return list;
        }
    }
}
