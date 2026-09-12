using MyApp.Modules.Elementos;
using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy;
using Microsoft.EntityFrameworkCore;

namespace MyApp.Modules;

/// <summary>
/// Dueño exclusivo del esquema de este módulo (mismo patrón que
/// <c>src/Platform/BitCode.Platform.Dashboard/DashboardDbContext.cs</c>): una única tabla propia
/// (<c>Elementos</c>) más <c>IdempotencyKeys</c>/<c>OutboxMessages</c>/<c>InboxMessages</c>, configuradas
/// automáticamente por <see cref="MultiTenantDbContext"/>. Ningún otro módulo debe leer/escribir esta
/// tabla directamente -- la única superficie pública son los contratos de
/// <c>ModuleNameEndpointRouteBuilderExtensions</c>.
/// </summary>
public sealed class ModuleNameDbContext(DbContextOptions<ModuleNameDbContext> options, ITenantProvider tenantProvider)
    : MultiTenantDbContext(options, tenantProvider)
{
    public DbSet<Elemento> Elementos => Set<Elemento>();
}
