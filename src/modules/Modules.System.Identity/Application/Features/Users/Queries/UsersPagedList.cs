using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Users.Queries
{
    public record UserPagedListQuery : PageFilter<User, UserPagedListDto>, IQuery<PagedList<UserPagedListDto>>
    {
    }

    public record UserPagedListDto : IMapFrom<User>, IQueryFilter<User>
    {
        public Guid Id { get; set; }
        public Guid? Avatar { get; set; }
        public string UserName { get; set; } = default!;

        public string Email { get; set; } = default!;
        public bool EmailConfirmed { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }

        public bool IsActive { get; set; } = true;
        public bool IsLockedOut { get; set; }
        
        public static IQueryable<User> Apply(IQueryable<User> query, string text)
        {
            return query.Where(x=>x.UserName.Contains(text)||x.FirstName!.Contains(text)||x.LastName!.Contains(text));
        }

        public void Mapping(Profile profile)
        {
            profile.CreateMap<User, UserPagedListDto>()
                .ForMember(x=>x.Avatar,opt=>opt.MapFrom(x=>x.ImageId));
        }
    }
    public class UserPagedListQueryHandler(IIdentityDbContext dbContext, IMapper mapper) : IQueryHandler<UserPagedListQuery, PagedList<UserPagedListDto>>
    {
        public async Task<PagedList<UserPagedListDto>> Handle(UserPagedListQuery request, CancellationToken cancellationToken)
        {
            var _list = dbContext.Users.AsNoTracking().Where(x=>x.UserType == UserType.Tenant);
            var res = await request.ToPaging(_list, mapper);
            return res;
        }
    }
}

