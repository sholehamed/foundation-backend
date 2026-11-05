using Modules.System.Identity.Application.Abstractions;

namespace Modules.System.Identity.Application.Features.Modules.Queries
{
    public record ModuleLookupQuery(string? text) : IQuery<List<LookupDto>>;
    public class ModuleLookupQueryHandler : IQueryHandler<ModuleLookupQuery, List<LookupDto>>
    {
        private readonly IIdentityDbContext _context;

        public ModuleLookupQueryHandler(IIdentityDbContext context)
        {
            _context = context;
        }

        public async Task<List<LookupDto>> Handle(ModuleLookupQuery request, CancellationToken cancellationToken)
        {
            var res = _context.Modules.Where(x => x.IsActive);
            if (!string.IsNullOrEmpty(request.text))
                res = res.Where(x => x.Title.Contains(request.text));
            var list = await res.Select(x => new LookupDto(x.Id, x.Title)).ToListAsync();
            return list;
        }
    }
}
