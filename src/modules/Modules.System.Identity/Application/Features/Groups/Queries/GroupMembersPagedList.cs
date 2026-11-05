using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Application.Features.Users.Queries;
using Modules.System.Identity.Domain.Entities;
namespace Groups.System.Identity.Application.Features.Groups.Queries
{
    public record GroupMembersPagedListQuery : PageFilter<UserGroup, GroupMemberPagedListDto>, IQuery<PagedList<GroupMemberPagedListDto>>
    {
        public Guid? GroupId { get; set; }
    }
    public record GroupMemberPagedListDto : IMapFrom<UserGroup>, IQueryFilter<UserGroup>
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public Guid? Avatar { get; set; }
        public string UserName { get; set; } = default!;

        public string FirstName { get; set; }
        public string LastName { get; set; }

        public bool IsActive { get; set; } = true;

        public static IQueryable<UserGroup> Apply(IQueryable<UserGroup> query, string text)
        {
            return query.Where(x => x.User.UserName.Contains(text) || x.User.FirstName!.Contains(text) || x.User.LastName!.Contains(text));
        }

        public void Mapping(Profile profile)
        {
            profile.CreateMap<UserGroup, GroupMemberPagedListDto>()
                .ForMember(x => x.UserId, opt => opt.MapFrom(x => x.UserId))
                .ForMember(x => x.UserName, opt => opt.MapFrom(x => x.User.UserName))
                .ForMember(x => x.FirstName, opt => opt.MapFrom(x => x.User.FirstName))
                .ForMember(x => x.LastName, opt => opt.MapFrom(x => x.User.LastName))
                .ForMember(x => x.Avatar, opt => opt.MapFrom(x => x.User.ImageId));
        }
    }
    public class GroupMemberPagedListQueryHandler(IIdentityDbContext dbContext, IMapper mapper) : IQueryHandler<GroupMembersPagedListQuery, PagedList<GroupMemberPagedListDto>>
    {
        public async Task<PagedList<GroupMemberPagedListDto>> Handle(GroupMembersPagedListQuery request, CancellationToken cancellationToken)
        {
            var _list = dbContext.UserGroups.AsNoTracking().Where(x=>x.GroupId==request.GroupId);
            var res = await request.ToPaging(_list, mapper);
            return res;
        }
    }
}
