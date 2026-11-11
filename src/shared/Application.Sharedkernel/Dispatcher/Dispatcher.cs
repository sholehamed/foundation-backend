using System.Diagnostics;
using Application.SharedKernel.Abstractions.Messaging;
using Domain.SharedKernel.Common.Events;
using Microsoft.Extensions.DependencyInjection;

namespace Application.SharedKernel.Dispatcher;

/// <summary>
/// Shared source-owned CQRS execution boundary. The Activity never captures
/// commands, DTO values, results, exception messages or user-controlled IDs.
/// Trace state is emitted only when the host has registered an ActivityListener.
/// </summary>
public sealed class Dispatcher(IServiceProvider serviceProvider) : IDispatcher
{
    private static readonly ActivitySource Source = new("Foundation.Cqrs");

    public async Task<TResult> Send<TResult>(
        ICommand<TResult> command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        using var activity = Begin("command", command.GetType());
        try
        {
            var handlerType = typeof(ICommandHandler<,>).MakeGenericType(command.GetType(), typeof(TResult));
            dynamic handler = serviceProvider.GetRequiredService(handlerType);
            dynamic actualCommand = command;
            TResult result = await handler.Handle(actualCommand, cancellationToken);
            return result;
        }
        catch (Exception exception)
        {
            MarkFailure(activity, exception);
            throw;
        }
    }

    public async Task Send(ICommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        using var activity = Begin("command", command.GetType());
        try
        {
            var handlerType = typeof(ICommandHandler<>).MakeGenericType(command.GetType());
            dynamic handler = serviceProvider.GetRequiredService(handlerType);
            dynamic actualCommand = command;
            await handler.Handle(actualCommand, cancellationToken);
        }
        catch (Exception exception)
        {
            MarkFailure(activity, exception);
            throw;
        }
    }

    public async Task<TResult> Query<TResult>(
        IQuery<TResult> query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        using var activity = Begin("query", query.GetType());
        try
        {
            var handlerType = typeof(IQueryHandler<,>).MakeGenericType(query.GetType(), typeof(TResult));
            dynamic handler = serviceProvider.GetRequiredService(handlerType);
            dynamic actualQuery = query;
            TResult result = await handler.Handle(actualQuery, cancellationToken);
            return result;
        }
        catch (Exception exception)
        {
            MarkFailure(activity, exception);
            throw;
        }
    }

    public async Task Publish<TNotification>(
        TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        ArgumentNullException.ThrowIfNull(notification);
        var handlers = serviceProvider.GetServices<INotificationHandler<TNotification>>();
        foreach (var handler in handlers)
        {
            using var activity = Begin("notification", typeof(TNotification));
            try
            {
                await handler.Handle(notification, cancellationToken);
            }
            catch (Exception exception)
            {
                MarkFailure(activity, exception);
                throw;
            }
        }
    }

    private static Activity? Begin(string category, Type request)
    {
        // The class name is compile-time application metadata, not a DTO,
        // route parameter, custom header or user-provided display string.
        var activity = Source.StartActivity("cqrs." + category, ActivityKind.Internal);
        activity?.SetTag("foundation.cqrs.kind", category);
        activity?.SetTag("foundation.cqrs.request_type", request.Name);
        return activity;
    }

    private static void MarkFailure(Activity? activity, Exception exception)
    {
        activity?.SetStatus(exception is OperationCanceledException
            ? ActivityStatusCode.Unset : ActivityStatusCode.Error);
        activity?.SetTag("foundation.error.type", exception.GetType().Name);
    }
}
