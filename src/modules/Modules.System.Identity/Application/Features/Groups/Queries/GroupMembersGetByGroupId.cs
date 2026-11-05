using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Application.Features.Users.Queries;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Groups.Queries
{
    public record GroupMembersGetByGroupIdQuery : FilterModel<UserGroup>, IQuery<PagedList<UserPagedListDto>>
    {
        public string Search { get; set; }
        public Guid GroupId { get; set; }
    }

    public class GroupMembersGetByGroupIdQueryHandler : IQueryHandler<GroupMembersGetByGroupIdQuery, PagedList<UserPagedListDto>>
    {
        private readonly IIdentityDbContext _context;
        private readonly IMapper mapper;

        public GroupMembersGetByGroupIdQueryHandler(IIdentityDbContext context, IMapper mapper)
        {
            _context = context;
            this.mapper = mapper;
        }

        public async Task<PagedList<UserPagedListDto>> Handle(GroupMembersGetByGroupIdQuery request, CancellationToken cancellationToken)
        {
            var count = _context.UserGroups.AsNoTracking().Where(x => x.GroupId == request.GroupId).Select(x => 1).Count();
            var query = _context.UserGroups.AsNoTracking().Where(x => x.GroupId == request.GroupId).Select(x => x.User).Skip((request.PageNumber-1) * request.PageSize).Take(request.PageSize).ProjectTo<UserPagedListDto>(mapper.ConfigurationProvider);
            var _resQuery = new PagedList<UserPagedListDto>(await query.ToListAsync(), count, request.PageNumber, request.PageSize);
            return _resQuery;
        }
    }
}
