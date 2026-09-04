using FluentAssertions;

namespace BitCode.Framework.Shared.Kernel.Tests;

public class ResultTests
{
    [Fact]
    public void Success_CreatesResultWithNoError()
    {
        var result = Result.Success();

        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Error.Should().Be(Error.None);
    }

    [Fact]
    public void Failure_CreatesResultWithGivenError()
    {
        var error = Error.Failure("Test.Error", "Algo salió mal");

        var result = Result.Failure(error);

        result.IsSuccess.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void Constructor_WithSuccessAndNonNoneError_Throws()
    {
        var act = () => new Result(true, Error.Failure("X", "Y"));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Constructor_WithFailureAndNoneError_Throws()
    {
        var act = () => new Result(false, Error.None);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GenericSuccess_ExposesValue()
    {
        var result = Result.Success(42);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void GenericFailure_AccessingValue_Throws()
    {
        var result = Result.Failure<int>(Error.Failure("Test.Error", "Algo salió mal"));

        var act = () => result.Value;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ImplicitConversion_FromValue_CreatesSuccessResult()
    {
        Result<int> result = 42;

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }
}
