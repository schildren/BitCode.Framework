using AppName;
using BitCode.Framework.Shared.Infrastructure.Web.Modularity;
using BitCode.Framework.Shared.Modularity;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddModules(builder.Configuration, typeof(Program).Assembly);

var app = builder.Build();

app.UseModules();

// EnsureCreated en vez de migraciones: esta es la aplicación base generada por "dotnet new bitcode-app",
// pensada como punto de partida de un proyecto consumidor real -- reemplazar por `dotnet ef migrations`
// antes de operar en producción (ningún proyecto del repositorio usa migraciones EF todavía, ver
// docs/plan-maestro-bitcode-ia.md, tarea F8-07, pendiente).
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();
}

app.Run();

public partial class Program;
