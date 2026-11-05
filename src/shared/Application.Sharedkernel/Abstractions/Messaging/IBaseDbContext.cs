namespace Application.SharedKernel.Abstractions.Messaging
{
    public interface IBaseDbContext
    {
        Task<int> SaveChangesAsync(CancellationToken cancellationToken=default);

    }
}
