using BitCode.Framework.Shared.Domain.Persistence;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace BitCode.Framework.Shared.Application.Tests;

public class ApplicationServiceCollectionExtensionsTests
{
    private static ServiceProvider BuildProvider(IUnitOfWork unitOfWork)
    {
        var services = new ServiceCollection();
        services.AddSingleton(unitOfWork);
        services.AddLogging();
        services.AddSharedApplication(typeof(TestCommand).Assembly);

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Send_Command_GoesThroughTransactionBehaviorAndCommits()
    {
        var unitOfWork = Substitute.For<IUnitOfWork>();
        using var provider = BuildProvider(unitOfWork);
        var mediator = provider.GetRequiredService<IMediator>();

        var result = await mediator.Send(new TestCommand("Mundo"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Hola, Mundo");
        await unitOfWork.Received(1).BeginTransactionAsync(Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Send_Query_DoesNotGoThroughTransactionBehavior()
    {
        var unitOfWork = Substitute.For<IUnitOfWork>();
        using var provider = BuildProvider(unitOfWork);
        var mediator = provider.GetRequiredService<IMediator>();

        var result = await mediator.Send(new TestQuery("Mundo"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Consulta: Mundo");
        await unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Send_Command_WithInvalidData_FailsValidationBeforeReachingHandlerOrTransaction()
    {
        var unitOfWork = Substitute.For<IUnitOfWork>();
        using var provider = BuildProvider(unitOfWork);
        var mediator = provider.GetRequiredService<IMediator>();

        var result = await mediator.Send(new TestCommand(string.Empty));

        result.IsFailure.Should().BeTrue();
        await unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
    }
}
