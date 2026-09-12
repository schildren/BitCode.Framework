using BitCode.Framework.Shared.Application.Behaviors;
using BitCode.Framework.Shared.Application.Idempotency;
using BitCode.Framework.Shared.Domain.Idempotency;
using BitCode.Framework.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace BitCode.Framework.Shared.Application.Tests;

public class IdempotencyBehaviorTests
{
    private static (
        IdempotencyBehavior<TestIdempotentCommand, Result<string>> Behavior,
        IIdempotencyKeyProvider KeyProvider,
        IIdempotencyStore Store) CreateBehavior(string? idempotencyKey = "key-1")
    {
        var keyProvider = Substitute.For<IIdempotencyKeyProvider>();
        keyProvider.IdempotencyKey.Returns(idempotencyKey);

        var store = Substitute.For<IIdempotencyStore>();
        store.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IdempotencyKey?>(null));

        var behavior = new IdempotencyBehavior<TestIdempotentCommand, Result<string>>(
            keyProvider,
            store,
            new IdempotencyOptions(),
            NullLogger<IdempotencyBehavior<TestIdempotentCommand, Result<string>>>.Instance);

        return (behavior, keyProvider, store);
    }

    [Fact]
    public async Task Handle_WithoutIdempotencyKey_ReturnsKeyRequiredFailureWithoutCallingNext()
    {
        var (behavior, _, store) = CreateBehavior(idempotencyKey: null);
        var nextCalled = false;

        var result = await behavior.Handle(
            new TestIdempotentCommand("Alpha"),
            () =>
            {
                nextCalled = true;
                return Task.FromResult(Result.Success("ok"));
            },
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(IdempotencyErrors.KeyRequired.Code);
        nextCalled.Should().BeFalse();
        await store.DidNotReceive().FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoExistingRecord_CallsNextAndStagesRecordOnSuccess()
    {
        var (behavior, _, store) = CreateBehavior();

        var result = await behavior.Handle(
            new TestIdempotentCommand("Alpha"),
            () => Task.FromResult(Result.Success("resultado")),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("resultado");
        store.Received(1).Add(Arg.Is<IdempotencyKey>(record =>
            record.Key == "key-1" && record.ResponseValueJson == "\"resultado\""));
    }

    [Fact]
    public async Task Handle_NoExistingRecord_HandlerFails_DoesNotStageAnyRecord()
    {
        var (behavior, _, store) = CreateBehavior();
        var error = Error.Failure("Test.Error", "Falló el handler");

        var result = await behavior.Handle(
            new TestIdempotentCommand("Alpha"),
            () => Task.FromResult(Result.Failure<string>(error)),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        store.DidNotReceive().Add(Arg.Any<IdempotencyKey>());
    }

    [Fact]
    public async Task Handle_ExistingRecordWithSameHash_ReturnsCachedResponseWithoutCallingNext()
    {
        var (behavior, _, store) = CreateBehavior();
        var command = new TestIdempotentCommand("Alpha");
        var requestHash = ComputeExpectedHash(command);
        store.FindAsync("key-1", Arg.Any<CancellationToken>()).Returns(Task.FromResult<IdempotencyKey?>(
            new IdempotencyKey
            {
                Id = Guid.NewGuid(),
                Key = "key-1",
                RequestHash = requestHash,
                ResponseValueJson = "\"resultado guardado\"",
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            }));
        var nextCalled = false;

        var result = await behavior.Handle(
            command,
            () =>
            {
                nextCalled = true;
                return Task.FromResult(Result.Success("otro resultado"));
            },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("resultado guardado");
        nextCalled.Should().BeFalse(
            "un reintento con la misma Idempotency-Key y el mismo payload no debe volver a ejecutar el handler");
        store.DidNotReceive().Add(Arg.Any<IdempotencyKey>());
    }

    [Fact]
    public async Task Handle_ExistingRecordWithDifferentHash_ReturnsKeyReusedFailureWithoutCallingNext()
    {
        var (behavior, _, store) = CreateBehavior();
        store.FindAsync("key-1", Arg.Any<CancellationToken>()).Returns(Task.FromResult<IdempotencyKey?>(
            new IdempotencyKey
            {
                Id = Guid.NewGuid(),
                Key = "key-1",
                RequestHash = "hash-de-otro-payload",
                ResponseValueJson = "\"resultado guardado\"",
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            }));
        var nextCalled = false;

        var result = await behavior.Handle(
            new TestIdempotentCommand("Alpha"),
            () =>
            {
                nextCalled = true;
                return Task.FromResult(Result.Success("otro resultado"));
            },
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(IdempotencyErrors.KeyReused.Code);
        nextCalled.Should().BeFalse(
            "reutilizar la misma Idempotency-Key con un payload distinto nunca debe ejecutarse como si " +
            "fuera el mismo reintento");
    }

    [Fact]
    public async Task Handle_ExistingRecordExpired_RemovesItAndExecutesAsIfItWereNew()
    {
        var (behavior, _, store) = CreateBehavior();
        var expiredRecord = new IdempotencyKey
        {
            Id = Guid.NewGuid(),
            Key = "key-1",
            RequestHash = "hash-que-no-importa-porque-esta-vencido",
            ResponseValueJson = "\"resultado viejo\"",
            CreatedAtUtc = DateTime.UtcNow.AddDays(-2),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1),
        };
        store.FindAsync("key-1", Arg.Any<CancellationToken>()).Returns(Task.FromResult<IdempotencyKey?>(expiredRecord));
        var nextCalled = false;

        var result = await behavior.Handle(
            new TestIdempotentCommand("Alpha"),
            () =>
            {
                nextCalled = true;
                return Task.FromResult(Result.Success("resultado nuevo"));
            },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("resultado nuevo");
        nextCalled.Should().BeTrue();
        store.Received(1).Remove(expiredRecord);
        store.Received(1).Add(Arg.Any<IdempotencyKey>());
    }

    [Fact]
    public async Task Handle_CommandWithoutValue_StagesRecordWithoutResponseValueJson()
    {
        var keyProvider = Substitute.For<IIdempotencyKeyProvider>();
        keyProvider.IdempotencyKey.Returns("key-1");
        var store = Substitute.For<IIdempotencyStore>();
        store.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IdempotencyKey?>(null));
        var behavior = new IdempotencyBehavior<TestIdempotentCommandWithoutValue, Result>(
            keyProvider,
            store,
            new IdempotencyOptions(),
            NullLogger<IdempotencyBehavior<TestIdempotentCommandWithoutValue, Result>>.Instance);

        var result = await behavior.Handle(
            new TestIdempotentCommandWithoutValue("Alpha"),
            () => Task.FromResult(Result.Success()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        store.Received(1).Add(Arg.Is<IdempotencyKey>(record => record.ResponseValueJson == null));
    }

    private static string ComputeExpectedHash<TRequest>(TRequest request)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(request, typeof(TRequest));
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash);
    }
}
