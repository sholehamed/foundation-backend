using Application.SharedKernel.Abstractions.Messaging;
using Domain.SharedKernel.Common.Events;
using Microsoft.Extensions.DependencyInjection;

namespace Application.SharedKernel.Dispatcher
{
    public sealed class Dispatcher : IDispatcher
    {
        private readonly IServiceProvider _serviceProvider;

        public Dispatcher(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async Task<TResult> Send<TResult>(
            ICommand<TResult> command,
            CancellationToken cancellationToken = default)
        {
            var commandType = command.GetType();
            var handlerType = typeof(ICommandHandler<,>).MakeGenericType(commandType, typeof(TResult));

            dynamic handler = _serviceProvider.GetRequiredService(handlerType);
            dynamic actualCommand = command;

            TResult result = await handler.Handle(actualCommand, cancellationToken);
            return result;
        }

        public async Task Send(
            ICommand command,
            CancellationToken cancellationToken = default)
        {
            var commandType = command.GetType();
            var handlerType = typeof(ICommandHandler<>).MakeGenericType(commandType);

            dynamic handler = _serviceProvider.GetRequiredService(handlerType);
            dynamic actualCommand = command;

            await handler.Handle(actualCommand, cancellationToken);
        }

        public async Task<TResult> Query<TResult>(
            IQuery<TResult> query,
            CancellationToken cancellationToken = default)
        {
            var queryType = query.GetType();
            var handlerType = typeof(IQueryHandler<,>).MakeGenericType(queryType, typeof(TResult));

            dynamic handler = _serviceProvider.GetRequiredService(handlerType);
            dynamic actualQuery = query;

            TResult result = await handler.Handle(actualQuery, cancellationToken);
            return result;
        }

        public async Task Publish<TNotification>(
            TNotification notification,
            CancellationToken cancellationToken = default)
            where TNotification : INotification
        {
            var handlers = _serviceProvider.GetServices<INotificationHandler<TNotification>>();

            foreach (var handler in handlers)
            {
                await handler.Handle(notification, cancellationToken);
            }
        }


    }
}
