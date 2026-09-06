namespace BitCode.Framework.Shared.Domain.Persistence;

/// <summary>
/// Excepción controlada de infraestructura (F1-08): la implementación de <see cref="IUnitOfWork"/>
/// la lanza cuando <c>SaveChangesAsync</c> detecta que el token de concurrencia de una o más
/// entidades cambió desde que se cargaron (por ejemplo, un <c>DbUpdateConcurrencyException</c> de
/// EF Core). Vive en <c>Shared.Domain</c> —junto a <see cref="IUnitOfWork"/>— para que
/// <c>Shared.Application</c> (que no referencia EF Core) pueda capturarla en <c>TransactionBehavior</c>
/// y traducirla de forma uniforme a un <c>Result.Failure</c>, sin que la capa de aplicación conozca
/// el tipo de excepción específico de EF Core.
/// </summary>
public class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
