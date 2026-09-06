namespace BitCode.Framework.Shared.Kernel;

/// <summary>
/// Código y <see cref="Error"/> bien definidos para un conflicto de concurrencia optimista (F1-08):
/// el <c>TransactionBehavior</c> del framework devuelve siempre este mismo <see cref="Error"/> cuando
/// un <c>SaveChangesAsync</c> falla porque el token de concurrencia (<see cref="IHasConcurrencyToken"/>)
/// de una entidad ya cambió desde que se cargó. <see cref="ErrorType.Conflict"/> se traduce en el
/// borde HTTP a <c>409 Conflict</c> (ver <c>ResultExtensions.ToProblemDetails</c> en
/// <c>Shared.Infrastructure.Web</c>), nunca a <c>500</c>.
/// </summary>
public static class ConcurrencyError
{
    public const string Code = "Concurrency.Conflict";

    public static readonly Error Conflict = Error.Conflict(
        Code,
        "El registro fue modificado por otra operación después de haberse cargado. Vuelva a cargarlo e intente nuevamente.");
}
