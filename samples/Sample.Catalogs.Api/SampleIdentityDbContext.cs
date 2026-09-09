using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Security.Identity;
using Microsoft.EntityFrameworkCore;

namespace Sample.Catalogs.Api;

/// <summary>
/// DbContext de identidad SOLO para este host de referencia (autenticación/RBAC vía Security 2.0) --
/// vive acá, no en <c>BitCode.Platform.Catalogs</c>, porque el módulo Catalogs and Parameters NO es
/// dueño de usuarios/roles (eso es responsabilidad de Identity Administration, Fase 6 módulo 1); un
/// consumidor real que necesite ambos módulos los compone en su propio host, cada uno con su propio
/// DbContext y base de datos, exactamente como se hace acá (ver <c>appsettings.json</c>,
/// <c>ConnectionStrings:Identity</c> vs. <c>ConnectionStrings:Default</c>), mismo patrón que
/// <c>Sample.Organization.Api/SampleIdentityDbContext.cs</c> (Fase 6, módulo 2).
/// </summary>
public sealed class SampleIdentityDbContext(DbContextOptions<SampleIdentityDbContext> options, ITenantProvider tenantProvider)
    : MultiTenantIdentityDbContext<ApplicationUser, ApplicationRole>(options, tenantProvider);
