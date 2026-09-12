using BitCode.Framework.Shared.Domain.MultiTenancy;
using BitCode.Framework.Shared.Infrastructure.Security.Identity;
using Microsoft.EntityFrameworkCore;

namespace BitCode.Framework.Platform.Identity;

/// <summary>
/// Dueño del esquema de identidad (AspNetUsers/AspNetRoles/AspNetUserRoles/AspNetUserClaims/
/// AspNetRoleClaims/RefreshTokens, más IdempotencyKeys/OutboxMessages -- ambos ya configurados por
/// <see cref="MultiTenantIdentityDbContext{TUser,TRole}"/>) del módulo Identity Administration (Fase 6,
/// módulo 1 del Plan Maestro). Ningún otro módulo de plataforma debe leer/escribir estas tablas
/// directamente: la única superficie pública para consultarlas/mutarlas son los contratos de este
/// módulo (comandos/queries mapeados por <c>IdentityAdministrationEndpointRouteBuilderExtensions</c>) o
/// los servicios de Security 2.0 ya expuestos (<c>UserManager&lt;ApplicationUser&gt;</c>/
/// <c>RoleManager&lt;ApplicationRole&gt;</c>/<c>IPermissionService</c>) -- ver
/// <c>docs/guia-identity-administration.md</c>.
/// <para>
/// Usa los tipos concretos <see cref="ApplicationUser"/>/<see cref="ApplicationRole"/> de Security 2.0
/// directamente, sin genéricos propios: un consumidor que necesite campos adicionales de usuario/rol
/// puede extender ese modelo en una tarea posterior (fuera del alcance de este primer corte, ver el
/// pendiente explícito en la guía).
/// </para>
/// </summary>
public sealed class IdentityAdministrationDbContext(
    DbContextOptions<IdentityAdministrationDbContext> options,
    ITenantProvider tenantProvider)
    : MultiTenantIdentityDbContext<ApplicationUser, ApplicationRole>(options, tenantProvider);
