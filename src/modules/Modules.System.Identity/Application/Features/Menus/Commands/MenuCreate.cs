using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Menus.Commands
{
    public record MenuCreateCommand : MapTo<Menu>, ICommand<Guid>
    {
        public Guid CategoryId { get; set; }
        public Guid? ParentId { get; set; }
        public bool IsActive { get; set; }
        public bool IsHidden { get; set; }
        public required string Title { get; set; }
        public string? Icon { get; set; }
        public short Order { get; set; }
        public string? Route { get; set; }
        public string? ExternalUrl { get; set; }
        public Guid? PermissionId { get; set; }
    }
    public class MenuCreateCommandValidator : AbstractValidator<MenuCreateCommand>
    {
        public MenuCreateCommandValidator()
        {
            RuleFor(x => x.Title)
            .NotEmpty()
            .MaximumLength(200);

            RuleFor(x => x.Order)
                .GreaterThanOrEqualTo<MenuCreateCommand, short>(0);

            RuleFor(x => x.CategoryId)
                .NotEmpty();
        }
    }
    public class MenuCreateCommandHandler(IIdentityDbContext dbContext, IMapper mapper) : ICommandHandler<MenuCreateCommand, Guid>
    {
        public async Task<Guid> Handle(MenuCreateCommand command, CancellationToken cancellationToken)
        {
            Menu entity = mapper.Map<Menu>(command);

            await dbContext.Menus.AddAsync(entity, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return entity.Id;
        }
    }
}
