using Microsoft.Extensions.Configuration;
﻿using Modules.System.Identity.Application.Abstractions;
using Modules.System.Identity.Domain.Entities;

namespace Modules.System.Identity.Application.Features.Tenants.Queries
{
    public record TenantResolveBySlugQuery(string slug) : IQuery<TenantResloveBySlugDto>;

    public record TenantResloveBySlugDto : MapFrom<Tenant>
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Code { get; set; }
        public string Slug { get; set; }
        public string Domain { get; set; }
        public Guid? Favicon { get; set; }
        public Guid? Logo { get; set; }
        public override void Mapping(Profile profile)
        {
            profile.CreateMap<Tenant, TenantResloveBySlugDto>()
                .ForMember(x => x.Code, opt => opt.MapFrom(x => GetTenantCode(x)));
        }
        public static string GetTenantCode(Tenant tenant)
        {
            string code = tenant.Slug;
            if (!string.IsNullOrEmpty(tenant.Domain))
            {
                code=code+"-"+tenant.Domain;
            }
            return code;
        }
    }


    public class TenantResolveBySlugQueryHandler : IQueryHandler<TenantResolveBySlugQuery, TenantResloveBySlugDto>
    {
        private readonly IIdentityDbContext _context;
        private readonly IMapper _mapper;
        private readonly IConfiguration _configuration;

        public TenantResolveBySlugQueryHandler(IIdentityDbContext context, IMapper mapper, IConfiguration configuration)
        {
            _context = context;
            _mapper = mapper;
            _configuration = configuration;
        }

        public async Task<TenantResloveBySlugDto> Handle(TenantResolveBySlugQuery request, CancellationToken cancellationToken)
        {
            // Match custom domains exactly. Only trusted base-domain subdomains
            // may resolve by their leading label; never resolve `slug.evil.com`.
            var host = (request.slug ?? "").Trim().ToLowerInvariant().TrimEnd('.');
            var baseDomain = (_configuration["Tenant:WorkspaceBaseDomain"] ?? "shinera.ir")
                .Trim().ToLowerInvariant().Trim('.');
            var suffix = "." + baseDomain;
            var slug = host.EndsWith(suffix, StringComparison.Ordinal)
                ? host[..^suffix.Length]
                : null;
            var tenantQuery = _context.Tenants.AsNoTracking()
                .Where(x => x.IsActive);
            var match = await tenantQuery.FirstOrDefaultAsync(x => x.Domain == host);
            if (match is null && slug is not null &&
                slug.Length > 0 && !slug.Contains('.'))
            {
                match = await tenantQuery.FirstOrDefaultAsync(x => x.Slug == slug);
            }
            if (match is null)
                throw new KeyNotFoundException("Workspace not found.");
            var tenant = _mapper.Map<TenantResloveBySlugDto>(match);
            return tenant;
        }
    }
}
