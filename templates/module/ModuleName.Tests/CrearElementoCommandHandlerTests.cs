using MyApp.Modules.Elementos;

namespace MyApp.Modules.Tests;

/// <summary>
/// Prueba unitaria del caso de uso (comando) del módulo -- sin infraestructura real, mismo criterio que
/// el resto de las pruebas unitarias del repositorio (ver <c>tests/Shared.Application.Tests</c>): el
/// handler no llama <c>SaveChangesAsync</c> (eso es responsabilidad de <c>TransactionBehavior</c> en el
/// pipeline real), así que probarlo con un repositorio en memoria alcanza para verificar la lógica de
/// negocio del caso de uso sin necesitar SQL Server.
/// </summary>
public sealed class CrearElementoCommandHandlerTests
{
    [Fact]
    public async Task Handle_ConNombreValido_AgregaElementoAlRepositorioYDevuelveSuId()
    {
        var repository = new FakeElementoRepository();
        var handler = new CrearElementoCommandHandler(repository);

        var result = await handler.Handle(new CrearElementoCommand("Prueba"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(repository.Elementos);
        Assert.Equal(result.Value, repository.Elementos[0].Id);
        Assert.Equal("Prueba", repository.Elementos[0].Nombre);
    }

    [Fact]
    public void Validator_ConNombreVacio_FallaLaValidacion()
    {
        var validator = new CrearElementoCommandValidator();

        var result = validator.Validate(new CrearElementoCommand(string.Empty));

        Assert.False(result.IsValid);
    }
}
