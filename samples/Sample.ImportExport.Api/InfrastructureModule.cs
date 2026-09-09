using BitCode.Framework.Platform.ImportExport;
using BitCode.Framework.Platform.ImportExport.Exportacion;
using BitCode.Framework.Platform.ImportExport.Importacion;
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
using Sample.ImportExport.Api.Clientes;

namespace Sample.ImportExport.Api;

/// <summary>
/// Aplicación de referencia de Import and Export (Fase 6, módulo 10) -- mismo patrón que
/// <c>Sample.IntegrationHub.Api/InfrastructureModule.cs</c> (módulo 9). Registra un tercer DbContext
/// propio del HOST (<see cref="SampleClientesDbContext"/>) para demostrar, con datos reales, los puntos de
/// extensión <see cref="IImportRowHandler"/>/<see cref="IExportDataSource"/> -- ninguno de los dos vive en
/// <c>BitCode.Platform.ImportExport</c> (ver sus respectivos <c>remarks</c>).
///
/// Tampoco registra <c>AddSharedBackgroundJobs</c>/<c>ImportBatchProcessorJob</c>/
/// <c>ExportBatchProcessorJob</c> -- el procesamiento por lotes se verifica en
/// <c>Sample.ImportExport.Api.Tests</c> invocando los jobs directamente contra el mismo
/// <see cref="ImportExportDbContext"/> (ver <c>docs/guia-import-export.md</c>, sección "Lotes", para cómo
/// los agregaría un consumidor productivo con Quartz HA real).
/// </summary>
public class InfrastructureModule : IWebFrameworkModule
{
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:Default en la configuración.");
        var identityConnectionString = configuration.GetConnectionString("Identity")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:Identity en la configuración.");
        var clientesConnectionString = configuration.GetConnectionString("Clientes")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:Clientes en la configuración.");

        services.AddHttpContextTenantProvider();

        services.AddSharedPersistence<ImportExportDbContext>(connectionString);

        services.AddDbContext<SampleClientesDbContext>(options => options.UseSqlServer(clientesConnectionString));
        services.AddScoped<IImportRowHandler, ClientesImportRowHandler>();
        services.AddScoped<IExportDataSource, ClientesExportDataSource>();

        services.AddDbContext<SampleIdentityDbContext>(options => options.UseSqlServer(identityConnectionString));
        services.AddSharedSecurity<
            BitCode.Framework.Shared.Infrastructure.Security.Identity.ApplicationUser,
            BitCode.Framework.Shared.Infrastructure.Security.Identity.ApplicationRole,
            SampleIdentityDbContext>(configuration);

        services.AddSharedAbacAuthorization();
        services.AddSharedAuditing();

        services.AddSharedImportExport(
            configureOptions: options => configuration.GetSection("ImportExport:Options").Bind(options),
            configureFileStore: options => configuration.GetSection("ImportExport:FileStore").Bind(options));

        services.AddHttpContextIdempotencyKeyProvider();

        // Handlers/validators de BitCode.Platform.ImportExport viven en su propio ensamblado.
        services.AddSharedApplication(
            typeof(InfrastructureModule).Assembly,
            typeof(ImportExportDbContext).Assembly);
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
