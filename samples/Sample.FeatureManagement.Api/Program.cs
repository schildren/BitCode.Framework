using BitCode.Framework.Platform.FeatureManagement;
using BitCode.Framework.Shared.Infrastructure.Web.Modularity;
using BitCode.Framework.Shared.Modularity;
using Sample.FeatureManagement.Api;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddModules(builder.Configuration, typeof(Program).Assembly);

var app = builder.Build();

app.UseModules();

// EnsureCreated en vez de migraciones (mismo criterio que Sample.Catalogs.Api/Sample.Organization.Api):
// este es un proyecto de referencia/demo de la plataforma, no un consumidor productivo -- ningún
// DbContext del repositorio usa `dotnet ef migrations` todavía (ver docs/guia-feature-management.md,
// sección "Pendientes").
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<FeatureManagementDbContext>().Database.EnsureCreatedAsync();
    await scope.ServiceProvider.GetRequiredService<SampleIdentityDbContext>().Database.EnsureCreatedAsync();
}

app.Run();

public partial class Program;
