using BitCode.Framework.Shared.Domain.MultiTenancy;
using Microsoft.AspNetCore.Http;

namespace BitCode.Framework.Shared.Infrastructure.Web.MultiTenancy;

/// <summary>
/// Implementación productiva de <see cref="ITenantProvider"/> (F1-12, estrategia T1 — base de datos
/// compartida con discriminador <c>TenantId</c>). Resuelve el <c>TenantId</c> exclusivamente desde
/// el claim <see cref="TenantClaimTypes.TenantId"/> del <see cref="HttpContext.User"/>, es decir, del
/// token JWT ya validado por el middleware de autenticación — nunca desde
/// <see cref="HttpContext.Request"/> (headers, query string, ruta), que el cliente controla
/// directamente sin pasar por autenticación (ver <c>docs/threat-model.md</c>, hallazgo S2).
/// </summary>
/// <remarks>
/// Cuándo usar cada <see cref="ITenantProvider"/>:
/// <list type="bullet">
/// <item>
/// <description><see cref="HttpContextTenantProvider"/>: proyectos multi-tenant con pipeline HTTP
/// (API real). Registrar con <c>AddHttpContextTenantProvider()</c> antes de
/// <c>AddSharedPersistence&lt;TContext&gt;</c> (que solo registra su valor por defecto con
/// <c>TryAddScoped</c>).</description>
/// </item>
/// <item>
/// <description><c>NullTenantProvider</c> (Shared.Infrastructure.Persistence): proyectos de un solo
/// tenant, o ejecuciones sin <see cref="HttpContext"/> (jobs en background, migraciones, seeders).
/// Deshabilita el filtro de tenancy por completo — nunca usarlo en un proyecto multi-tenant
/// real.</description>
/// </item>
/// </list>
/// Si un job en background necesita resolver el tenant de forma aislada (p. ej. procesar una cola
/// por tenant), debe implementar su propio <see cref="ITenantProvider"/> a partir del mensaje/job
/// data, no reutilizar <see cref="HttpContextTenantProvider"/> (que siempre devuelve "sin tenant"
/// fuera de un request HTTP) — ese diseño queda fuera del alcance de esta tarea (ver F1-15).
/// </remarks>
public sealed class HttpContextTenantProvider(IHttpContextAccessor httpContextAccessor) : ITenantProvider
{
    /// <summary>
    /// Siempre <see langword="true"/>: este provider existe específicamente para habilitar el
    /// aislamiento multi-tenant en producción. Un proyecto que no necesite multi-tenancy debe usar
    /// <c>NullTenantProvider</c> en su lugar, no este provider con datos de un único tenant.
    /// </summary>
    public bool IsMultiTenancyEnabled => true;

    public Guid? TenantId => ResolveTenantId();

    private Guid? ResolveTenantId()
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            // Sin HttpContext (p. ej. un job en background resuelto en el mismo contenedor de DI)
            // este provider no tiene de dónde leer el tenant: falla cerrado devolviendo null
            // (MultiTenantDbContext lo trata como Guid.Empty, que no coincide con ningún tenant
            // real), nunca abre la multi-tenencia ni asume un tenant por defecto.
            return null;
        }

        if (httpContext.User.Identity?.IsAuthenticated != true)
        {
            // Request anónimo: no hay tenant que resolver. Si el endpoint requiere autenticación,
            // la autorización ya debería haber rechazado el request antes de llegar hasta acá; si es
            // un endpoint público, no debería consultar datos de un tenant específico.
            return null;
        }

        var claim = httpContext.User.FindFirst(TenantClaimTypes.TenantId);
        if (claim is null || !Guid.TryParse(claim.Value, out var tenantId))
        {
            // Nunca se asume un tenant por defecto ante la ausencia/invalidez del claim: fallar con
            // una excepción controlada (500 genérico vía GlobalExceptionHandler) es preferible a
            // arriesgar una fuga de datos entre tenants por un fallback silencioso.
            throw new TenantResolutionException(
                $"El usuario autenticado no tiene un claim de tenant válido ('{TenantClaimTypes.TenantId}'). " +
                "El TenantId nunca se resuelve con un valor por defecto.");
        }

        return tenantId;
    }
}
