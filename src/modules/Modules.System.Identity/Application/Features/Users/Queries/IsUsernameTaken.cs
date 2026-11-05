using Modules.System.Identity.Application.Abstractions;

namespace Modules.System.Identity.Application.Features.Users.Queries
{
    public record IsUsernameTakenQuery(Guid? userId,string username) : IQuery<bool>
    {
    }

    
    public class IsUsernameTakenQueryHandler(IIdentityDbContext context) : IQueryHandler<IsUsernameTakenQuery, bool>
    {
        public async Task<bool> Handle(IsUsernameTakenQuery request, CancellationToken cancellationToken)
        {

            var res = await context.Users.AsNoTracking().AnyAsync(x => (request.userId==Guid.Empty||x.Id!=request.userId)&& x.NormalizedUserName == request.username.Trim().ToUpperInvariant());
                
            return res;
        }
    }
}

