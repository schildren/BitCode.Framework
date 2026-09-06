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

    private static (TransactionBehavior<TestTransactionalCommand, Result<string>>, IUnitOfWork) CreateTransactionalBehavior()
    {
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var behavior = new TransactionBehavior<TestTransactionalCommand, Result<string>>(
            unitOfWork,
            NullLogger<TransactionBehavior<TestTransactionalCommand, Result<string>>>.Instance);

        return (behavior, unitOfWork);
    }

    [Fact]
    public async Task Handle_SimpleCommand_WhenHandlerSucceeds_SavesChangesWithoutOpeningTransaction()
    {
        var (behavior, unitOfWork) = CreateBehavior();

        var result = await behavior.Handle(
            new TestCommand("Alpha"),
            () => Task.FromResult(Result.Success("ok")),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().RollbackAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SimpleCommand_WhenHandlerReturnsFailureResult_DoesNotSaveChanges()
    {
        var (behavior, unitOfWork) = CreateBehavior();
        var error = Error.Failure("Test.Error", "Falló el handler");

        var result = await behavior.Handle(
            new TestCommand("Alpha"),
            () => Task.FromResult(Result.Failure<string>(error)),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SimpleCommand_WhenHandlerThrows_DoesNotOpenTransactionAndRethrows()
    {
        var (behavior, unitOfWork) = CreateBehavior();

        var act = async () => await behavior.Handle(
            new TestCommand("Alpha"),
            () => throw new InvalidOperationException("Boom"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().RollbackAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TransactionalCommand_WhenHandlerSucceeds_BeginsAndCommitsTransaction()
    {
        var (behavior, unitOfWork) = CreateTransactionalBehavior();

        var result = await behavior.Handle(
            new TestTransactionalCommand("Alpha"),
            () => Task.FromResult(Result.Success("ok")),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await unitOfWork.Received(1).BeginTransactionAsync(Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().RollbackAsync(Arg.Any<CancellationToken>());
        // El behavior nunca llama SaveChangesAsync directamente en el camino transaccional: la
        // persistencia queda enteramente delegada a un único CommitAsync (F1-07).
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TransactionalCommand_WhenHandlerReturnsFailureResult_RollsBackWithoutCommitting()
    {
        var (behavior, unitOfWork) = CreateTransactionalBehavior();
        var error = Error.Failure("Test.Error", "Falló el handler");

        var result = await behavior.Handle(
            new TestTransactionalCommand("Alpha"),
            () => Task.FromResult(Result.Failure<string>(error)),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        await unitOfWork.Received(1).BeginTransactionAsync(Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        // Ante un Result fallido, el behavior no debe haber persistido nada: ni SaveChangesAsync
        // directo ni CommitAsync (que internamente hace SaveChangesAsync) llegan a ejecutarse (F1-07).
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TransactionalCommand_WhenHandlerThrows_RollsBackAndRethrows()
    {
        var (behavior, unitOfWork) = CreateTransactionalBehavior();

        var act = async () => await behavior.Handle(
            new TestTransactionalCommand("Alpha"),
            () => throw new InvalidOperationException("Boom"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await unitOfWork.Received(1).BeginTransactionAsync(Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
