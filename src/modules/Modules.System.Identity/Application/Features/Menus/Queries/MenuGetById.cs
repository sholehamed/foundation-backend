using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Menus.Queries;

public record MenuGetByIdQuery : IQuery<MenuGetByIdDto>
{
    public Guid Id { get; init; }

    public MenuGetByIdQuery() { }
    public MenuGetByIdQuery(Guid id) => Id = id;
}

/// <summary>
/// Canonical read contract matches Menu and the Angular menu editor.
/// Deprecated read aliases remain temporarily for older consumers; do not use
/// them for new writes. Remove only after a versioned contract migration.
/// </summary>
public record MenuGetByIdDto : MapFrom<Menu>
{
    public Guid Id { get; init; }
    public Guid CategoryId { get; init; }
    public Guid? ParentId { get; init; }
    public bool IsActive { get; init; }
    public bool IsHidden { get; init; }
    public string Title { get; init; } = default!;
    public string? Icon { get; init; }
    public short Order { get; init; }
    public string? Route { get; init; }
    public string? ExternalUrl { get; init; }
    public Guid? PermissionId { get; init; }

    // Read-only compatibility for previously returned JSON: pMenuId / url.
    [Obsolete("Use ParentId (parentId in JSON).")]
    public Guid? PMenuId => ParentId;

    [Obsolete("Use Route (route in JSON).")]
    public string? Url => Route;
}

public sealed class MenuGetByIdQueryHandler(IIdentityDbContext context, IMapper mapper)
    : IQueryHandler<MenuGetByIdQuery, MenuGetByIdDto>
{
    public async Task<MenuGetByIdDto> Handle(MenuGetByIdQuery request, CancellationToken cancellationToken)
    {
        var entity = await context.Menus.AsNoTracking()
            .Where(x => x.Id == request.Id)
            .ProjectTo<MenuGetByIdDto>(mapper.ConfigurationProvider)
            .FirstOrDefaultAsync(cancellationToken);
        Guard.Against.NotFound(request.Id, entity);
        return entity;
    }
}
