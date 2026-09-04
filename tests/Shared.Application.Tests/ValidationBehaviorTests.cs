using BitCode.Framework.Shared.Application.Behaviors;
using BitCode.Framework.Shared.Kernel;
using FluentAssertions;
using FluentValidation;

namespace BitCode.Framework.Shared.Application.Tests;

public class ValidationBehaviorTests
{
    private static ValidationBehavior<TestCommand, Result<string>> CreateBehavior(
        params IValidator<TestCommand>[] validators) =>
        new(validators);

    [Fact]
    public async Task Handle_WithoutValidators_CallsNext()
    {
        var behavior = CreateBehavior();
        var nextCalled = false;

        var result = await behavior.Handle(
            new TestCommand("Alpha"),
            () =>
            {
                nextCalled = true;
                return Task.FromResult(Result.Success("ok"));
            },
            CancellationToken.None);

        nextCalled.Should().BeTrue();
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithPassingValidators_CallsNext()
    {
        var behavior = CreateBehavior(new TestCommandValidator());
        var nextCalled = false;

        var result = await behavior.Handle(
            new TestCommand("Alpha"),
            () =>
            {
                nextCalled = true;
                return Task.FromResult(Result.Success("ok"));
            },
            CancellationToken.None);

        nextCalled.Should().BeTrue();
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WithFailingValidators_ReturnsFailureWithoutCallingNext()
    {
        var behavior = CreateBehavior(new TestCommandValidator());
        var nextCalled = false;

        var result = await behavior.Handle(
            new TestCommand(string.Empty),
            () =>
            {
                nextCalled = true;
                return Task.FromResult(Result.Success("ok"));
            },
            CancellationToken.None);

        nextCalled.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<ValidationError>();
        ((ValidationError)result.Error).Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Handle_WithMultipleValidatorsWhereOneFails_ReturnsFailure()
    {
        var behavior = CreateBehavior(new TestCommandValidator(), new NoOpTestCommandValidator());
        var nextCalled = false;

        var result = await behavior.Handle(
            new TestCommand(string.Empty),
            () =>
            {
                nextCalled = true;
                return Task.FromResult(Result.Success("ok"));
            },
            CancellationToken.None);

        nextCalled.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
    }
}
