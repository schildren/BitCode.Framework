using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Security.Identity;
using Microsoft.EntityFrameworkCore;

namespace Sample.Documents.Api;

/// <summary>
/// DbContext de identidad SOLO para este host de referencia (autenticación/RBAC vía Security 2.0) --
/// vive acá, no en <c>BitCode.Platform.Documents</c>, porque el módulo Documents NO es dueño de
/// usuarios/roles (eso es responsabilidad de Identity Administration, Fase 6 módulo 1); un consumidor
/// real que necesite ambos módulos los compone en su propio host, cada uno con su propio DbContext y base
/// de datos, exactamente como se hace acá (ver <c>appsettings.json</c>, <c>ConnectionStrings:Identity</c>
/// vs. <c>ConnectionStrings:Default</c>), mismo patrón que <c>Sample.FeatureManagement.Api/SampleIdentityDbContext.cs</c>
/// (Fase 6, módulo 4).
/// </summary>
public sealed class SampleIdentityDbContext(DbContextOptions<SampleIdentityDbContext> options, ITenantProvider tenantProvider)
    : MultiTenantIdentityDbContext<ApplicationUser, ApplicationRole>(options, tenantProvider);
