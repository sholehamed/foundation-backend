using Domain.SharedKernel.Common.Events;

namespace Application.SharedKernel.Abstractions.Messaging
{
    public interface IDispatcher
    {
        Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default);
        Task Send(ICommand command, CancellationToken cancellationToken = default);

        Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default);

        Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification;

    }
}
