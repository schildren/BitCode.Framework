using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BitCode.Framework.Shared.Infrastructure.Persistence.MultiTenancy;

/// <summary>
/// El filtro global de MultiTenantDbContext cierra sobre el estado de la instancia (_tenantId,
/// _isMultiTenancyEnabled) capturado en OnModelCreating. El IModelCacheKeyFactory por defecto de
/// EF Core cachea el modelo (y por tanto ese closure) por tipo de DbContext, reutilizándolo para
/// TODAS las instancias posteriores — congelando el tenant de la primera instancia creada. Usar la
/// instancia misma como clave de caché fuerza a reconstruir el modelo (y el filtro) en cada
/// DbContext, a costa de un pequeño overhead de arranque por instancia.
/// </summary>
public sealed class PerInstanceModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) => (context, designTime);
}
