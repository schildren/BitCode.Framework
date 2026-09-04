using FluentAssertions;

namespace BitCode.Framework.Shared.Kernel.Tests;

public class ValidationErrorTests
{
    [Fact]
    public void FromErrors_AggregatesGivenErrors()
    {
        var errors = new[]
        {
            Error.Validation("Nombre.Vacio", "El nombre es obligatorio"),
            Error.Validation("Monto.Negativo", "El monto no puede ser negativo"),
        };

        var validationError = ValidationError.FromErrors(errors);

        validationError.Errors.Should().BeEquivalentTo(errors);
        validationError.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public void ValidationError_IsAssignableToError()
    {
        var validationError = ValidationError.FromErrors([Error.Validation("X", "Y")]);

        Error error = validationError;

        error.Should().NotBeNull();
    }
}
