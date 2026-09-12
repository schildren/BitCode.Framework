using BitCode.Framework.Platform.Identity;
using BitCode.Framework.Shared.Infrastructure.Web.Modularity;
using BitCode.Framework.Shared.Modularity;
using Sample.IdentityAdmin.Api;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddModules(builder.Configuration, typeof(Program).Assembly);

var app = builder.Build();

app.UseModules();

// EnsureCreated en vez de migraciones (mismo criterio que Sample.Api/Program.cs): este es un proyecto
// de referencia/demo de la plataforma, no un consumidor productivo -- ningún DbContext del repositorio
// (Sample.Api incluido) usa `dotnet ef migrations` todavía, ver docs/guia-identity-administration.md,
// sección "Pendientes". Verifica en runtime, contra SQL Server real, que el modelo completo de
// IdentityAdministrationDbContext (Identity + Idempotency + Outbox + filtros de tenant/auditoría) se
// materializa sin error -- el criterio de aceptación real que se puede demostrar en este estado del
// framework.
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<IdentityAdministrationDbContext>().Database.EnsureCreatedAsync();
}

app.Run();

public partial class Program;
