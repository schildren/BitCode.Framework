using BitCode.Framework.Shared.Application.Behaviors;
using BitCode.Framework.Shared.Kernel;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace BitCode.Framework.Shared.Application.Tests;

public class LoggingBehaviorTests
{
    [Fact]
    public async Task Handle_OnSuccess_ReturnsResponseUnchanged()
    {
        var behavior = new LoggingBehavior<TestCommand, Result<string>>(
            NullLogger<LoggingBehavior<TestCommand, Result<string>>>.Instance);

        var result = await behavior.Handle(
            new TestCommand("Alpha"),
            () => Task.FromResult(Result.Success("ok")),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("ok");
    }

    [Fact]
    public async Task Handle_OnFailure_ReturnsResponseUnchanged()
    {
        var behavior = new LoggingBehavior<TestCommand, Result<string>>(
            NullLogger<LoggingBehavior<TestCommand, Result<string>>>.Instance);
        var error = Error.Failure("Test.Error", "Falló");

        var result = await behavior.Handle(
            new TestCommand("Alpha"),
            () => Task.FromResult(Result.Failure<string>(error)),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }
}
