using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Ordinis.Application.Common;

namespace Ordinis.UnitTests.Application.Common;

/// <summary>
/// Verifies <see cref="Dispatcher"/> validation pipeline behaviour:
/// commands are validated before the handler runs, queries bypass validation,
/// missing handlers throw, and a missing validator is a silent no-op.
/// </summary>
public class DispatcherTests
{
    // -------------------------------------------------------------------------
    // Minimal command / query stubs used only within this test class
    // -------------------------------------------------------------------------

    private sealed record PingCommand(string Message) : ICommand<string>;
    private sealed record SilentCommand : ICommand;
    private sealed record BadCommand(string Message) : ICommand<string>;
    private sealed record SomeQuery(int Value) : IQuery<int>;
    private sealed record OrphanCommand : ICommand;

    private sealed class PingHandler : ICommandHandler<PingCommand, string>
    {
        public Task<string> HandleAsync(PingCommand command, CancellationToken ct = default)
            => Task.FromResult($"pong:{command.Message}");
    }

    private sealed class SilentHandler : ICommandHandler<SilentCommand>
    {
        public bool Invoked { get; private set; }
        public Task HandleAsync(SilentCommand command, CancellationToken ct = default)
        {
            Invoked = true;
            return Task.CompletedTask;
        }
    }

    private sealed class BadHandler : ICommandHandler<BadCommand, string>
    {
        public bool Invoked { get; private set; }
        public Task<string> HandleAsync(BadCommand command, CancellationToken ct = default)
        {
            Invoked = true;
            return Task.FromResult("should not reach here");
        }
    }

    private sealed class SomeQueryHandler : IQueryHandler<SomeQuery, int>
    {
        public Task<int> HandleAsync(SomeQuery query, CancellationToken ct = default)
            => Task.FromResult(query.Value * 2);
    }

    // Validator that always fails — used to assert queries bypass validation.
    private sealed class AlwaysFailQueryValidator : AbstractValidator<SomeQuery>
    {
        public AlwaysFailQueryValidator()
        {
            RuleFor(q => q.Value).Must(_ => false).WithMessage("intentionally failing");
        }
    }

    // Validator that rejects BadCommand.
    private sealed class BadCommandValidator : AbstractValidator<BadCommand>
    {
        public BadCommandValidator()
        {
            RuleFor(c => c.Message).NotEmpty().WithMessage("Message must not be empty.");
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static Dispatcher BuildDispatcher(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        configure(services);
        IServiceProvider provider = services.BuildServiceProvider();
        return new Dispatcher(provider);
    }

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SendAsync_ValidCommandWithPassingValidator_InvokesHandlerAndReturnsResult()
    {
        Dispatcher dispatcher = BuildDispatcher(services =>
        {
            services.AddScoped<ICommandHandler<PingCommand, string>, PingHandler>();
            // No validator registered — this also tests the no-validator path.
        });

        string result = await dispatcher.SendAsync<PingCommand, string>(new PingCommand("hello"));

        Assert.Equal("pong:hello", result);
    }

    [Fact]
    public async Task SendAsync_InvalidCommand_ThrowsValidationExceptionBeforeHandlerRuns()
    {
        var handler = new BadHandler();

        Dispatcher dispatcher = BuildDispatcher(services =>
        {
            services.AddScoped<ICommandHandler<BadCommand, string>>(_ => handler);
            services.AddScoped<IValidator<BadCommand>, BadCommandValidator>();
        });

        Ordinis.Application.Common.ValidationException ex =
            await Assert.ThrowsAsync<Ordinis.Application.Common.ValidationException>(
                () => dispatcher.SendAsync<BadCommand, string>(new BadCommand("")));

        Assert.False(handler.Invoked, "Handler must not be invoked when validation fails.");
        Assert.True(ex.Errors.ContainsKey("Message"),
            "ValidationException must carry the field-level error.");
    }

    [Fact]
    public async Task SendAsync_ValidCommandWithNoRegisteredValidator_ReachesHandlerDirectly()
    {
        var handler = new SilentHandler();

        Dispatcher dispatcher = BuildDispatcher(services =>
        {
            services.AddScoped<ICommandHandler<SilentCommand>>(_ => handler);
            // Intentionally no IValidator<SilentCommand> registered.
        });

        await dispatcher.SendAsync(new SilentCommand());

        Assert.True(handler.Invoked);
    }

    [Fact]
    public async Task QueryAsync_BypassesValidationEvenWhenValidatorIsRegistered()
    {
        // AlwaysFailQueryValidator is registered but must never run for a query.
        Dispatcher dispatcher = BuildDispatcher(services =>
        {
            services.AddScoped<IQueryHandler<SomeQuery, int>, SomeQueryHandler>();
            services.AddScoped<IValidator<SomeQuery>, AlwaysFailQueryValidator>();
        });

        // Should complete without a ValidationException because QueryAsync
        // does not run the validation pipeline.
        int result = await dispatcher.QueryAsync<SomeQuery, int>(new SomeQuery(5));

        Assert.Equal(10, result);
    }

    [Fact]
    public async Task SendAsync_NoRegisteredHandler_ThrowsInvalidOperationException()
    {
        Dispatcher dispatcher = BuildDispatcher(_ => { /* nothing registered */ });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.SendAsync(new OrphanCommand()));
    }
}
