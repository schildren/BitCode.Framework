using BitCode.Framework.Shared.Infrastructure.Observability;
using BitCode.Framework.Shared.Infrastructure.Web.Modularity;
using BitCode.Framework.Shared.Modularity;
using Sample.Api;

var builder = WebApplication.CreateBuilder(args);

// F4-10: Serilog reemplaza el logger por defecto de ASP.NET Core -- WriteTo.Console() siempre activo
// (comportamiento visible sin cambios para quien corre este proyecto localmente); agrega
// WriteTo.OpenTelemetry(...) SOLO si "OpenTelemetry:OtlpEndpoint" está configurado (ver
// SerilogHostBuilderExtensions), cerrando la brecha de "Centralizar exportación de logs" (F4-10) para
// este proyecto de referencia -- mismo patrón que AddSharedObservability ya usa para trazas/métricas.
builder.Host.UseSharedSerilog();

// F4-12 (cierre del gap de hot-reload, ver docs/politica-configuracion-y-feature-flags.md sección 2.4):
// carga la sección "FeatureFlags" desde un archivo JSON con reloadOnChange -- comportamiento nativo de
// Microsoft.Extensions.Configuration.Json (dispara el change token que IOptionsMonitor<FeatureFlagsOptions>
// necesita para notificar sin reiniciar el proceso). "optional: true" es obligatorio: en desarrollo local
// (sin el volumen de k8s/sample-api/base/featureflags-configmap.yaml montado) el archivo no existe, y el
// proyecto debe seguir arrancando con los flags en su valor por defecto (ninguno declarado). La ruta es
// configurable vía "FeatureFlags:ConfigFilePath" (appsettings/variable de entorno) para no atar el código a
// la convención de montaje de un único cluster; "/app/config/featureflags.json" es el default productivo,
// coincide con el "mountPath" del volumeMount declarado en el Deployment.
var featureFlagsConfigPath = builder.Configuration["FeatureFlags:ConfigFilePath"] ?? "/app/config/featureflags.json";
builder.Configuration.AddJsonFile(featureFlagsConfigPath, optional: true, reloadOnChange: true);

builder.Services.AddModules(builder.Configuration, typeof(Program).Assembly);

var app = builder.Build();

app.UseModules();

// EnsureCreated en vez de migraciones: este es un proyecto piloto/demo, no un consumidor real.
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<SampleDbContext>().Database.EnsureCreatedAsync();
}

app.Run();

public partial class Program;
