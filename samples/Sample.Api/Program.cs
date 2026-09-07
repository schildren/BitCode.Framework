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
