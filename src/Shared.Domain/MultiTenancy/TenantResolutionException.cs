namespace BitCode.Framework.Shared.Domain.MultiTenancy;

/// <summary>
/// Excepción controlada (F1-12): una implementación productiva de <see cref="ITenantProvider"/>
/// (p. ej. <c>HttpContextTenantProvider</c> en Shared.Infrastructure.Web) la lanza cuando un request
/// autenticado que requiere aislamiento multi-tenant no trae un claim <see cref="TenantClaimTypes.TenantId"/>
/// válido. El framework nunca traduce esta situación a un tenant por defecto ni a "sin filtro": la
/// excepción se propaga sin capturar hasta <c>GlobalExceptionHandler</c> (500 genérico), priorizando
/// fallar de forma segura sobre fallar de forma silenciosa con fuga de datos entre tenants.
/// </summary>
public class TenantResolutionException : Exception
{
    public TenantResolutionException(string message) : base(message)
    {
    }
}
