using BitCode.Framework.Platform.Notifications;
using BitCode.Framework.Shared.Application;
using BitCode.Framework.Shared.Infrastructure.Observability;
using BitCode.Framework.Shared.Infrastructure.Persistence;
using BitCode.Framework.Shared.Infrastructure.Security;
using BitCode.Framework.Shared.Infrastructure.Security.Abac;
using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Infrastructure.Web;
using BitCode.Framework.Shared.Infrastructure.Web.HealthChecks;
using BitCode.Framework.Shared.Infrastructure.Web.Modularity;
using BitCode.Framework.Shared.Infrastructure.Web.OpenApi;
using BitCode.Framework.Shared.Modularity;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Sample.Notifications.Api;

/// <summary>
/// Aplicación de referencia de Notifications (Fase 6, módulo 8) -- mismo patrón que
/// <c>Sample.TaskInbox.Api/InfrastructureModule.cs</c> (Fase 6, módulo 7). Deliberadamente NO registra
/// <c>AddSharedPersistence&lt;WorkflowDbContext&gt;</c> en el mismo host -- mismo hallazgo ya documentado
/// por Task Inbox (combinar dos <c>AddSharedPersistence&lt;T&gt;</c> de dos bounded contexts de Fase 6 en
/// un mismo proceso es hoy inseguro, ver <c>docs/guia-taskinbox.md</c>, sección "Límites conocidos"): el
/// evento de Workflow que <c>TareaAsignadaNotificationEventConsumer</c> consume se simula en las pruebas
/// de integración construyendo directamente el <c>record</c> público de <c>BitCode.Platform.Workflow</c>.
///
/// Tampoco registra <c>AddSharedBackgroundJobs</c>/<c>NotificationRetryJob</c> -- el retry se verifica en
/// <c>Sample.Notifications.Api.Tests</c> invocando el job directamente contra el mismo
/// <see cref="NotificationsDbContext"/> (ver <c>docs/guia-notifications.md</c>, sección "Retry", para
/// cómo lo agregaría un consumidor productivo con Quartz HA real).
/// </summary>
public class InfrastructureModule : IWebFrameworkModule
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:Default en la configuración.");
        var identityConnectionString = configuration.GetConnectionString("Identity")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:Identity en la configuración.");

        services.AddHttpContextTenantProvider();

        services.AddSharedPersistence<NotificationsDbContext>(connectionString);

        services.AddDbContext<SampleIdentityDbContext>(options => options.UseSqlServer(identityConnectionString));
        services.AddSharedSecurity<
            BitCode.Framework.Shared.Infrastructure.Security.Identity.ApplicationUser,
            BitCode.Framework.Shared.Infrastructure.Security.Identity.ApplicationRole,
            SampleIdentityDbContext>(configuration);

        services.AddSharedAbacAuthorization();
        services.AddSharedAuditing();

        services.AddSharedNotifications(
            smtp => configuration.GetSection("Notifications:Smtp").Bind(smtp),
            options => configuration.GetSection("Notifications:Retry").Bind(options.Retry));

        services.AddHttpContextIdempotencyKeyProvider();

        // Handlers/validators de BitCode.Platform.Notifications viven en su propio ensamblado.
        services.AddSharedApplication(
            typeof(InfrastructureModule).Assembly,
            typeof(NotificationsDbContext).Assembly);
        services.AddSharedExceptionHandling();

        services.AddSharedApiVersioning();
        services.AddSharedOpenApiForApiVersion(1, options => options.AddJwtBearerSecurityScheme());

        services.AddSharedObservability(configuration);
    }

    public void ConfigureApplication(WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapSharedHealthChecks();
        app.MapOpenApi();
    }
}
