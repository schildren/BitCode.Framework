using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Security.Identity;
using Microsoft.EntityFrameworkCore;

namespace Sample.Dashboard.Api;

/// <summary>
/// DbContext de identidad SOLO para este host de referencia (autenticación/RBAC vía Security 2.0) --
/// vive acá, no en <c>BitCode.Platform.Dashboard</c>, mismo patrón que
/// <c>Sample.IntegrationHub.Api/SampleIdentityDbContext.cs</c> (Fase 6, módulo 9).
/// </summary>
public sealed class SampleIdentityDbContext(DbContextOptions<SampleIdentityDbContext> options, ITenantProvider tenantProvider)
    : MultiTenantIdentityDbContext<ApplicationUser, ApplicationRole>(options, tenantProvider);
