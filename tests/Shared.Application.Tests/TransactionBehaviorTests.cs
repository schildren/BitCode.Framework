using BitCode.Framework.Shared.Application.Behaviors;
using BitCode.Framework.Shared.Domain.Persistence;
using BitCode.Framework.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace BitCode.Framework.Shared.Application.Tests;

public class TransactionBehaviorTests
{
    private static (TransactionBehavior<TestCommand, Result<string>>, IUnitOfWork) CreateBehavior()
    {
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var behavior = new TransactionBehavior<TestCommand, Result<string>>(
            unitOfWork,
            NullLogger<TransactionBehavior<TestCommand, Result<string>>>.Instance);

        return (behavior, unitOfWork);
    }

    [Fact]
    public async Task Handle_WhenHandlerSucceeds_BeginsAndCommitsTransaction()
    {
        var (behavior, unitOfWork) = CreateBehavior();

        var result = await behavior.Handle(
            new TestCommand("Alpha"),
            () => Task.FromResult(Result.Success("ok")),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await unitOfWork.Received(1).BeginTransactionAsync(Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().RollbackAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenHandlerReturnsFailureResult_RollsBackWithoutCommitting()
    {
        var (behavior, unitOfWork) = CreateBehavior();
        var error = Error.Failure("Test.Error", "Falló el handler");

        var result = await behavior.Handle(
            new TestCommand("Alpha"),
            () => Task.FromResult(Result.Failure<string>(error)),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        await unitOfWork.Received(1).BeginTransactionAsync(Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenHandlerThrows_RollsBackAndRethrows()
    {
        var (behavior, unitOfWork) = CreateBehavior();

        var act = async () => await behavior.Handle(
            new TestCommand("Alpha"),
            () => throw new InvalidOperationException("Boom"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await unitOfWork.Received(1).BeginTransactionAsync(Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }
}
