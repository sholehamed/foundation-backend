using Application.SharedKernel.Models;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Microsoft.EntityFrameworkCore;

namespace Application.SharedKernel.Abstractions.Mapping;

public static class MappingExtensions
{
    public static async Task<PagedList<TDestination>> PaginatedListAsync<TDestination>(
        this IQueryable<TDestination> queryable, int pageNumber, int pageSize) where TDestination : class
    {
        var count=await queryable.CountAsync();
        var list=await queryable.ToListAsync();
        return new PagedList<TDestination>(list,count, pageNumber, pageSize);
    }

    public static Task<List<TDestination>> ProjectToListAsync<TDestination>(this IQueryable queryable,
        IConfigurationProvider configuration) where TDestination : class
    {
        return queryable.ProjectTo<TDestination>(configuration).AsNoTracking().ToListAsync();
    }
}