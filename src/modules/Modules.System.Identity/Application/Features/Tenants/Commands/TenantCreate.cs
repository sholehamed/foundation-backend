using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Tenants.Commands
{
    public record TenantCreateCommand : MapTo<Tenant>, ICommand<Guid>
    {
        public string Name { get; init; }
        public string? Domain { get; set; }
        public Guid? Logo { get; set; }
        public Guid? Favicon { get; set; }
        public string Slug { get; init; }
        public bool IsActive { get; init; }
    }
    public class TenantCreateCommandValidator : AbstractValidator<TenantCreateCommand>
    {
        public TenantCreateCommandValidator()
        {

        }
    }
    public class TenantCreateCommandHandler : ICommandHandler<TenantCreateCommand, Guid>
    {
        private readonly IIdentityDbContext _dbContext;
        private readonly IMapper _mapper;

        public TenantCreateCommandHandler(IIdentityDbContext dbContext, IMapper mapper)
        {
            _dbContext = dbContext;
            _mapper = mapper;
        }

        public async Task<Guid> Handle(TenantCreateCommand command, CancellationToken cancellationToken)
        {
            Tenant entity = _mapper.Map<Tenant>(command);
            await _dbContext.Tenants.AddAsync(entity);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return entity.Id;
        }
    }

}
