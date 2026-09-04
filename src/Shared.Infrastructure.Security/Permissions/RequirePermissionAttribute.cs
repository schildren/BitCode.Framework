using Microsoft.AspNetCore.Authorization;

namespace BitCode.Framework.Shared.Infrastructure.Security.Permissions;

/// <summary>
/// [RequirePermission("productos.crear")] equivale a [Authorize(Policy = "productos.crear")], que
/// PermissionAuthorizationPolicyProvider resuelve dinámicamente contra PermissionRequirement.
/// </summary>
public class RequirePermissionAttribute(string permission) : AuthorizeAttribute(permission);
