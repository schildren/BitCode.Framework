namespace BitCode.Framework.Shared.Infrastructure.Security.Permissions;

/// <summary>
/// Invalidación explícita del cache de permisos (F2-09). Un proyecto consumidor que modifica los
/// permisos de un rol (<see cref="RoleManagerPermissionExtensions.AddPermissionAsync{TRole}"/>/
/// <see cref="RoleManagerPermissionExtensions.RemovePermissionAsync{TRole}"/>) o la asignación de roles
/// de un usuario (<c>UserManager.AddToRoleAsync</c>/<c>RemoveFromRoleAsync</c>, o cualquier otro cambio
/// que afecte qué permisos puede ejercer un usuario concreto) debe invocar el método correspondiente
/// inmediatamente después de que la mutación se confirme -- de lo contrario, la entrada cacheada sigue
/// vigente hasta <see cref="PermissionCacheOptions.Expiration"/> (fail-closed acotado, nunca
/// indefinido, pero no inmediato sin esta llamada explícita). No es un mecanismo declarativo/automático a
/// propósito: mismo criterio que <c>IAuthorizationPolicyEvaluator</c> (F2-08), el framework no puede
/// interceptar genéricamente cualquier forma en que un proyecto consumidor decida mutar roles/permisos
/// (Identity expone esas operaciones directamente vía <c>UserManager</c>/<c>RoleManager</c>, sin ningún
/// punto de extensión común).
/// </summary>
/// <remarks>
/// Registrado por defecto como <see cref="NullPermissionCacheInvalidator"/> (no-op) por
/// <see cref="PermissionEvaluationServiceCollectionExtensions.AddSharedPermissionEvaluation"/> -- un
/// proyecto que todavía no llamó a
/// <see cref="PermissionCacheServiceCollectionExtensions.AddSharedPermissionCache"/> puede seguir
/// inyectando <see cref="IPermissionCacheInvalidator"/> sin condicionar su código a si el cache está
/// habilitado (mismo patrón que <c>NullTenantProvider</c>/<c>NullPermissionService</c>).
/// <see cref="PermissionCacheServiceCollectionExtensions.AddSharedPermissionCache"/> reemplaza este
/// registro por la instancia real (<c>CachedPermissionService</c>, que implementa esta interfaz).
/// </remarks>
public interface IPermissionCacheInvalidator
{
    /// <summary>
    /// Elimina la entrada cacheada de permisos efectivos del usuario <paramref name="userId"/> -- llamar
    /// después de cualquier cambio que afecte qué roles/permisos tiene ESE usuario en particular (alta o
    /// baja de un rol, por ejemplo).
    /// </summary>
    ValueTask InvalidateUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Elimina la entrada cacheada de permisos del rol <paramref name="roleName"/> -- llamar después de
    /// agregar o quitar un permiso a ESE rol. No invalida en cascada los permisos efectivos ya cacheados
    /// de los usuarios que tienen ese rol (ver <see cref="PermissionCacheOptions.Expiration"/> para el
    /// límite de esa ventana): acotar esa cascada requeriría rastrear la membresía rol-usuarios, que
    /// Identity no expone de forma eficiente y que esta tarea (F2-09) no incorpora.
    /// </summary>
    ValueTask InvalidateRoleAsync(string roleName, CancellationToken cancellationToken = default);
}
