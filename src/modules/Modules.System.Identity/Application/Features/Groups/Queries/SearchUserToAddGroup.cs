using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Application.Features.Users.Queries;

namespace Modules.System.Identity.Application.Features.Groups.Queries
{
    public record SearchUserToAddGroupQuery:IQuery<List<UserPagedListDto>>
    {
        public string Search { get; set; }
        public Guid GroupId { get; set; }
        public int PageSize { get; set; }
    }
    public class SearchUserToAddGroupQueryHandler : IQueryHandler<SearchUserToAddGroupQuery, List<UserPagedListDto>>
    {
        private readonly IIdentityDbContext _context;
        private readonly IMapper _mapper;

        public SearchUserToAddGroupQueryHandler(IIdentityDbContext context, IMapper mapper)
        {
            _context = context;
            _mapper = mapper;
        }

        public async Task<List<UserPagedListDto>> Handle(SearchUserToAddGroupQuery request, CancellationToken cancellationToken)
        {
            var excludeUsers = await _context.UserGroups.AsNoTracking().Where(x => x.GroupId == request.GroupId).Select(x => x.UserId).ToListAsync();
            var res = await _context.Users.Where(u => !excludeUsers.Contains(u.Id))
                .Where(x=>x.FirstName.Contains(request.Search)||x.LastName.Contains(request.Search)||x.UserName.Contains(request.Search)).Take(20)
                .ProjectTo<UserPagedListDto>(_mapper.ConfigurationProvider).ToListAsync();
            return res;
        }
    }
}
