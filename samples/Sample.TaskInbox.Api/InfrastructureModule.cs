using BitCode.Framework.Platform.TaskInbox;
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

namespace Sample.TaskInbox.Api;

/// <summary>
/// Aplicación de referencia de Task Inbox (Fase 6, módulo 7) -- mismo patrón que
/// <c>Sample.Workflow.Api/InfrastructureModule.cs</c> (Fase 6, módulo 6). Deliberadamente NO registra
/// <c>AddSharedPersistence&lt;WorkflowDbContext&gt;</c> en el mismo host: combinar dos
/// <c>AddSharedPersistence&lt;T&gt;</c> de dos bounded contexts de Fase 6 distintos en un único proceso
/// es hoy inseguro (hallazgo de esta tarea, ver <c>docs/guia-taskinbox.md</c>, sección "Límites
/// conocidos") -- <c>PersistenceServiceCollectionExtensions.AddSharedPersistence&lt;TContext&gt;</c>
/// registra <c>services.AddScoped&lt;DbContext&gt;(...)</c> (no <c>TryAdd</c>) apuntando al
/// <typeparamref name="TContext"/> concreto de esa llamada; una segunda llamada para otro
/// <c>MultiTenantDbContext</c> pisa esa resolución para TODO el contenedor (gana la última
/// registrada), así que <c>RepositoryBase&lt;TEntity,TId&gt;</c> (que depende de <c>DbContext</c> sin
/// tipar) terminaría resolviendo el <c>DbSet&lt;TEntity&gt;</c> equivocado para uno de los dos módulos.
/// Este host es, por diseño, Task Inbox en aislamiento: los tres eventos de integración de Workflow que
/// consume se simulan en las pruebas de integración (<c>Sample.TaskInbox.Api.Tests</c>) construyendo
/// directamente los <c>record</c> públicos de <c>BitCode.Platform.Workflow</c> -- exactamente como
/// llegarían de un despliegue real de Workflow vía Kafka, sin que este proceso dependa de
/// <c>WorkflowDbContext</c> en ningún momento.
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

        services.AddSharedPersistence<TaskInboxDbContext>(connectionString);

        services.AddDbContext<SampleIdentityDbContext>(options => options.UseSqlServer(identityConnectionString));
        services.AddSharedSecurity<
            BitCode.Framework.Shared.Infrastructure.Security.Identity.ApplicationUser,
            BitCode.Framework.Shared.Infrastructure.Security.Identity.ApplicationRole,
            SampleIdentityDbContext>(configuration);

        services.AddSharedAbacAuthorization();
        services.AddSharedAuditing();

        services.AddSharedTaskInbox();

        services.AddHttpContextIdempotencyKeyProvider();

        // Handlers/validators de BitCode.Platform.TaskInbox viven en su propio ensamblado.
        services.AddSharedApplication(
            typeof(InfrastructureModule).Assembly,
            typeof(TaskInboxDbContext).Assembly);
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
