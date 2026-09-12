using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Security.Identity;
using Microsoft.EntityFrameworkCore;

namespace Sample.TaskInbox.Api;

/// <summary>
/// DbContext de identidad SOLO para este host de referencia (autenticación/RBAC vía Security 2.0) --
/// vive acá, no en <c>BitCode.Platform.TaskInbox</c>, mismo patrón que
/// <c>Sample.Workflow.Api/SampleIdentityDbContext.cs</c> (Fase 6, módulo 6).
/// </summary>
public sealed class SampleIdentityDbContext(DbContextOptions<SampleIdentityDbContext> options, ITenantProvider tenantProvider)
    : MultiTenantIdentityDbContext<ApplicationUser, ApplicationRole>(options, tenantProvider);
