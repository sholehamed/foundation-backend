using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Tenants.Commands
{
    public record TenantUpdateCommand : MapTo<Tenant>, ICommand
    {
        public Guid Id { get; set; }
        public string Name { get; init; }
        public string? Domain { get; set; }
        public Guid? Logo { get; set; }
        public Guid? Favicon { get; set; }
        public string Slug { get; init; }
        public bool IsActive { get; init; }
    }
    public class TenantUpdateCommandValidator : AbstractValidator<TenantUpdateCommand>
    {
        public TenantUpdateCommandValidator()
        {

        }
    }
    public class TenantUpdateCommandHandler : ICommandHandler<TenantUpdateCommand>
    {
        private readonly IIdentityDbContext _context;
        private readonly IMapper _mapper;

        public TenantUpdateCommandHandler(IIdentityDbContext identityDbContext, IMapper mapper)
        {
            _context = identityDbContext;
            _mapper = mapper;
        }

        public async Task Handle(TenantUpdateCommand command, CancellationToken cancellationToken)
        {
            Tenant? entity = await _context.Tenants
       .FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken);

            Guard.Against.NotFound(command.Id, entity);


            _mapper.Map(command, entity);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

}

