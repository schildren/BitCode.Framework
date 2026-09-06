using BitCode.Framework.Shared.Domain.Idempotency;
using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Web.Exceptions;
using BitCode.Framework.Shared.Infrastructure.Web.Idempotency;
using BitCode.Framework.Shared.Infrastructure.Web.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Web;

public static class WebServiceCollectionExtensions
{
    /// <summary>
    /// Registra GlobalExceptionHandler + ProblemDetails (RFC 7807). El proyecto consumidor todavía
    /// debe llamar app.UseExceptionHandler() en el pipeline (Program.cs) para activarlo.
    /// </summary>
    public static IServiceCollection AddSharedExceptionHandling(this IServiceCollection services)
    {
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();

        return services;
    }

    /// <summary>
    /// Registra <see cref="HttpContextTenantProvider"/> como implementación productiva de
    /// <see cref="ITenantProvider"/> (F1-12). Debe llamarse ANTES de
    /// <c>AddSharedPersistence&lt;TContext&gt;</c> en el <c>InfrastructureModule</c> del proyecto
    /// consumidor: ese método solo registra su valor por defecto (<c>NullTenantProvider</c>) con
    /// <c>TryAddScoped</c>, así que una llamada previa a este método gana. Solo aplica a proyectos
    /// multi-tenant reales con pipeline HTTP y autenticación JWT que emite el claim
    /// <see cref="TenantClaimTypes.TenantId"/> (ver <see cref="HttpContextTenantProvider"/> para el
    /// resto de los escenarios).
    /// </summary>
    public static IServiceCollection AddHttpContextTenantProvider(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ITenantProvider, HttpContextTenantProvider>();

        return services;
    }

    /// <summary>
    /// Registra <see cref="HttpContextIdempotencyKeyProvider"/> como implementación productiva de
    /// <see cref="IIdempotencyKeyProvider"/> (F1-22). Debe llamarse ANTES de
    /// <c>AddSharedApplication</c> en el <c>InfrastructureModule</c> del proyecto consumidor: ese
    /// método solo registra su valor por defecto (<c>NullIdempotencyKeyProvider</c>) con
    /// <c>TryAddScoped</c>, así que una llamada previa a este método gana — mismo patrón que
    /// <see cref="AddHttpContextTenantProvider"/>.
    /// </summary>
    public static IServiceCollection AddHttpContextIdempotencyKeyProvider(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IIdempotencyKeyProvider, HttpContextIdempotencyKeyProvider>();

        return services;
    }

    /// <summary>
    /// Registra <c>HealthCheckService</c> (F1-25) sin ningún check propio. Un proyecto que ya llama
    /// <c>AddSharedPersistence</c>/<c>AddSharedCaching</c> no necesita invocar este método: ambos
    /// registran sus propios checks ("sql-server"/"redis", tag "ready") llamando también
    /// <c>services.AddHealthChecks()</c>, que es idempotente entre sí. Este método existe para un
    /// proyecto sin persistencia/cache que igual quiera exponer <c>/health/live</c> vía
    /// <see cref="HealthChecks.HealthCheckEndpointRouteBuilderExtensions.MapSharedHealthChecks"/> —
    /// sin al menos una llamada a <c>AddHealthChecks()</c> en algún punto del registro,
    /// <c>MapHealthChecks</c> no puede resolver <c>HealthCheckService</c> del contenedor. Ver
    /// <c>docs/guia-health-checks.md</c>.
    /// </summary>
    public static IServiceCollection AddSharedHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks();

        return services;
    }
}
