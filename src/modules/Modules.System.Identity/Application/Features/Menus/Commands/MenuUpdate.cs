using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Menus.Commands;

public record MenuUpdateCommand : ICommand
{
    public Guid Id { get; set; }
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

public sealed class MenuUpdateCommandValidator : AbstractValidator<MenuUpdateCommand>
{
    public MenuUpdateCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.CategoryId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Order).GreaterThanOrEqualTo((short)0);
        RuleFor(x => x).Must(x =>
                string.IsNullOrWhiteSpace(x.Route) || string.IsNullOrWhiteSpace(x.ExternalUrl))
            .WithMessage("A menu cannot have both a route and an external URL.");
        RuleFor(x => x).Must(x => x.ParentId != x.Id)
            .WithMessage("A menu cannot be its own parent.");
    }
}

public sealed class MenuUpdateCommandHandler(IIdentityDbContext context)
    : ICommandHandler<MenuUpdateCommand>
{
    public async Task Handle(MenuUpdateCommand command, CancellationToken cancellationToken)
    {
        // Never load Users by the Menu ID. Update exactly the requested Menu entity.
        var menu = await context.Menus
            .FirstOrDefaultAsync(x => x.Id == command.Id, cancellationToken);
        Guard.Against.NotFound(command.Id, menu);

        menu.CategoryId = command.CategoryId;
        menu.ParentId = command.ParentId;
        menu.IsActive = command.IsActive;
        menu.IsHidden = command.IsHidden;
        menu.Title = command.Title;
        menu.Icon = command.Icon;
        menu.Order = command.Order;
        menu.Route = command.Route;
        menu.ExternalUrl = command.ExternalUrl;
        menu.PermissionId = command.PermissionId;

        await context.SaveChangesAsync(cancellationToken);
    }
}
