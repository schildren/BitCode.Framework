using BitCode.Framework.Shared.Infrastructure.Web.Results;
using BitCode.Framework.Shared.Kernel;
using FluentAssertions;

namespace BitCode.Framework.Shared.Infrastructure.Web.Tests;

public class ResultExtensionsTests
{
    [Fact]
    public void ToProblemDetails_OnSuccessResult_Throws()
    {
        var result = Result.Success();

        var act = () => result.ToProblemDetails();

        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(ErrorType.NotFound, 404)]
    [InlineData(ErrorType.Conflict, 409)]
    [InlineData(ErrorType.Unauthorized, 401)]
    [InlineData(ErrorType.Forbidden, 403)]
    [InlineData(ErrorType.Failure, 500)]
    public void ToProblemDetails_MapsErrorTypeToExpectedStatusCode(ErrorType errorType, int expectedStatusCode)
    {
        var error = new Error("Test.Error", "Descripción", errorType);
        var result = Result.Failure(error);

        var httpResult = result.ToProblemDetails();

        httpResult.Should().BeAssignableTo<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
        ((Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult)httpResult).StatusCode.Should().Be(expectedStatusCode);
    }

    [Fact]
    public void ToProblemDetails_WithValidationError_ReturnsValidationProblem()
    {
        var validationError = ValidationError.FromErrors(
            [Error.Validation("Nombre.Vacio", "El nombre es obligatorio")]);
        var result = Result.Failure(validationError);

        var httpResult = result.ToProblemDetails();

        httpResult.Should().BeAssignableTo<Microsoft.AspNetCore.Http.HttpResults.ValidationProblem>();
    }

    [Fact]
    public void ToOkOrProblem_OnSuccess_ReturnsOk()
    {
        var result = Result.Success(42);

        var httpResult = result.ToOkOrProblem();

        httpResult.Should().BeAssignableTo<Microsoft.AspNetCore.Http.HttpResults.Ok<int>>();
    }

    [Fact]
    public void ToOkOrProblem_OnFailure_ReturnsProblem()
    {
        var result = Result.Failure<int>(Error.NotFound("Producto.NoEncontrado", "No existe"));

        var httpResult = result.ToOkOrProblem();

        httpResult.Should().BeAssignableTo<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>();
    }
}
