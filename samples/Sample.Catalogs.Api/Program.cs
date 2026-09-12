using BitCode.Framework.Platform.Catalogs;
using BitCode.Framework.Shared.Infrastructure.Web.Modularity;
using BitCode.Framework.Shared.Modularity;
using Sample.Catalogs.Api;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddModules(builder.Configuration, typeof(Program).Assembly);

var app = builder.Build();

app.UseModules();

// EnsureCreated en vez de migraciones (mismo criterio que Sample.Organization.Api/Sample.Api): este es
// un proyecto de referencia/demo de la plataforma, no un consumidor productivo -- ningún DbContext del
// repositorio usa `dotnet ef migrations` todavía (ver docs/guia-catalogs.md, sección "Pendientes").
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<CatalogsDbContext>().Database.EnsureCreatedAsync();
    await scope.ServiceProvider.GetRequiredService<SampleIdentityDbContext>().Database.EnsureCreatedAsync();
}

app.Run();

public partial class Program;
