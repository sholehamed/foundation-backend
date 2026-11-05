using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Users.Queries
{
    public record AdminPagedListQuery : PageFilter<User, UserPagedListDto>, IQuery<PagedList<UserPagedListDto>>
    {
    }
    
    public class AdminPagedListQueryHandler(IIdentityDbContext dbContext, IMapper mapper) : IQueryHandler<AdminPagedListQuery, PagedList<UserPagedListDto>>
    {
        public async Task<PagedList<UserPagedListDto>> Handle(AdminPagedListQuery request, CancellationToken cancellationToken)
        {
            var _list = dbContext.Users.AsNoTracking().Where(x=>x.UserType==UserType.System);
            var res = await request.ToPaging(_list, mapper);
            return res;
        }
    }
}

