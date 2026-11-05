using Modules.System.Identity.Application.Abstractions;
namespace Groups.System.Identity.Application.Features.Groups.Queries
{
    public record GroupLookupQuery(string? text) : IQuery<List<LookupDto>>;
    public class GroupLookupQueryHandler : IQueryHandler<GroupLookupQuery, List<LookupDto>>
    {
        private readonly IIdentityDbContext _context;

        public GroupLookupQueryHandler(IIdentityDbContext context)
        {
            _context = context;
        }

        public async Task<List<LookupDto>> Handle(GroupLookupQuery request, CancellationToken cancellationToken)
        {
            var res = _context.Groups.Where(x => x.IsActive);
            if (!string.IsNullOrEmpty(request.text))
                res = res.Where(x => x.Name.Contains(request.text));
            var list = await res.Select(x => new LookupDto(x.Id, x.Name)).ToListAsync();
            return list;
        }
    }
}
