using FluentAssertions;

namespace BitCode.Framework.Shared.Kernel.Tests;

/// <summary>
/// F1-21: <see cref="PageRequest.Create"/> es el punto de validación principal de límites de
/// paginación — un <c>pageSize</c> fuera de rango nunca se trunca en silencio, siempre devuelve un
/// <see cref="Result{TValue}"/> fallido con <see cref="ErrorType.Validation"/>.
/// </summary>
public class PageRequestTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 100)]
    [InlineData(5, 50)]
    public void Create_WithValidPageAndPageSize_Succeeds(int page, int pageSize)
    {
        var result = PageRequest.Create(page, pageSize);

        result.IsSuccess.Should().BeTrue();
        result.Value.Page.Should().Be(page);
        result.Value.PageSize.Should().Be(pageSize);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_WithPageBelowMinimum_FailsWithValidationError(int page)
    {
        var result = PageRequest.Create(page, pageSize: 10);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be("Paginacion.PaginaInvalida");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Create_WithPageSizeBelowMinimum_FailsWithValidationError(int pageSize)
    {
        var result = PageRequest.Create(page: 1, pageSize);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be("Paginacion.TamanioInvalido");
    }

    [Fact]
    public void Create_WithPageSizeAboveDefaultMax_FailsWithValidationError_InsteadOfTruncatingSilently()
    {
        var result = PageRequest.Create(page: 1, pageSize: 10_000);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be("Paginacion.TamanioExcedeLimite");
        result.Error.Description.Should().Contain("10000").And.Contain("100");
    }

    [Fact]
    public void Create_WithPageSizeAboveCustomMax_FailsWithValidationError()
    {
        var result = PageRequest.Create(page: 1, pageSize: 25, maxPageSize: 20);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Paginacion.TamanioExcedeLimite");
    }

    [Fact]
    public void Create_WithPageSizeEqualToMax_Succeeds()
    {
        var result = PageRequest.Create(page: 1, pageSize: PageRequest.DefaultMaxPageSize);

        result.IsSuccess.Should().BeTrue();
    }
}
