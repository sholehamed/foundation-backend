namespace Application.SharedKernel.Abstractions.Messaging
{
    public interface ICommandDispatcher
    {
        Task Send(ICommand command, CancellationToken cancellationToken = default);
        Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default);
    }
}
