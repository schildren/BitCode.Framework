using BitCode.Framework.Platform.Notifications;
using BitCode.Framework.Shared.Infrastructure.Web.Modularity;
using BitCode.Framework.Shared.Modularity;
using Sample.Notifications.Api;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddModules(builder.Configuration, typeof(Program).Assembly);

var app = builder.Build();

app.UseModules();

// EnsureCreated en vez de migraciones (mismo criterio que Sample.TaskInbox.Api): este es un proyecto de
// referencia/demo de la plataforma, no un consumidor productivo.
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Database.EnsureCreatedAsync();
    await scope.ServiceProvider.GetRequiredService<SampleIdentityDbContext>().Database.EnsureCreatedAsync();
}

app.Run();

public partial class Program;
