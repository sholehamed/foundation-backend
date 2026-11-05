using AutoMapper;

namespace Application.SharedKernel.Abstractions.Mapping;

public interface IMapFrom<T>
{
    void Mapping(Profile profile);

}
public interface IQueryFilter<T>
{
    static abstract IQueryable<T> Apply(IQueryable<T> query,string? text);
}
public record MapFrom<T> : IMapFrom<T>
{
    public virtual void Mapping(Profile profile)
    {
        var dest = GetType();
        profile.CreateMap(typeof(T), dest);
    }
}
public record MapTo<T> : IMapFrom<T>
{
    public virtual void Mapping(Profile profile)
    {
        var dest = GetType();
        profile.CreateMap(dest, typeof(T));
    }
}