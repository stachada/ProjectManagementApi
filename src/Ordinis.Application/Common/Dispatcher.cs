using System.Collections.Concurrent;
using System.Reflection;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Ordinis.Application.Common;

/// <inheritdoc cref="IDispatcher"/>
internal sealed class Dispatcher : IDispatcher
{
    private readonly IServiceProvider serviceProvider;

    public Dispatcher(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider;
    }

    /// <inheritdoc/>
    public async Task SendAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : ICommand
    {
        await ValidateAsync(command, cancellationToken);

        ICommandHandler<TCommand> handler = serviceProvider.GetRequiredService<ICommandHandler<TCommand>>();
        await handler.HandleAsync(command, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<TResult> SendAsync<TCommand, TResult>(TCommand command, CancellationToken cancellationToken = default)
        where TCommand : ICommand<TResult>
    {
        await ValidateAsync(command, cancellationToken);

        ICommandHandler<TCommand, TResult> handler = serviceProvider.GetRequiredService<ICommandHandler<TCommand, TResult>>();
        return await handler.HandleAsync(command, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<TResult> QueryAsync<TQuery, TResult>(TQuery query, CancellationToken cancellationToken = default)
        where TQuery : IQuery<TResult>
    {
        await ValidateAsync(query, cancellationToken);

        IQueryHandler<TQuery, TResult> handler = serviceProvider.GetRequiredService<IQueryHandler<TQuery, TResult>>();
        return await handler.HandleAsync(query, cancellationToken);
    }

    #region Private helpers
    private async Task ValidateAsync<T>(T instance, CancellationToken cancellationToken)
        where T : notnull
    {
        // Validators are optional, If none is registered, skip silently
        IValidator<T>? validator = serviceProvider.GetService<IValidator<T>>();
        if (validator is null)
        {
            return;
        }

        ValidationResult result = await validator.ValidateAsync(instance, cancellationToken);
        if (result.IsValid)
        {
            return;
        }

        var errors = result.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.ErrorMessage).ToArray()
            );

        throw new ValidationException(errors);
    }
    #endregion
}
