namespace Application.SharedKernel.Abstractions.Messaging
{
    public interface IQueryDispatcher
    {
        Task<TResult> Send<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default);
    }
}
