using BitCode.Framework.Platform.Documents;
using BitCode.Framework.Shared.Infrastructure.Web.Modularity;
using BitCode.Framework.Shared.Modularity;
using Sample.Documents.Api;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddModules(builder.Configuration, typeof(Program).Assembly);

var app = builder.Build();

app.UseModules();

// EnsureCreated en vez de migraciones (mismo criterio que Sample.FeatureManagement.Api/Sample.Catalogs.Api):
// este es un proyecto de referencia/demo de la plataforma, no un consumidor productivo -- ningún DbContext
// del repositorio usa `dotnet ef migrations` todavía (ver docs/guia-documents.md, sección "Pendientes").
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<DocumentsDbContext>().Database.EnsureCreatedAsync();
    await scope.ServiceProvider.GetRequiredService<SampleIdentityDbContext>().Database.EnsureCreatedAsync();
}

app.Run();

public partial class Program;
