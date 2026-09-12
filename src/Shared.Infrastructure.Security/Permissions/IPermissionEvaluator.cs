using System.Security.Claims;

namespace BitCode.Framework.Shared.Infrastructure.Security.Permissions;

/// <summary>
/// Evaluador de permisos efectivos (F2-07, RBAC 2.0): dado el <see cref="ClaimsPrincipal"/> ya
/// autenticado de la request actual, calcula el conjunto de permisos que puede ejercer — combinando
/// roles/permisos de Identity local (<see cref="IPermissionService"/>), permisos declarados
/// directamente como claim del token (IdP externo, F2-01) y el scope OAuth2 del token cuando tiene
/// forma de permiso (F2-04, Client Credentials). Es el evaluador sobre el que se apoya
/// <see cref="PermissionAuthorizationHandler"/> (reemplaza la llamada directa a
/// <see cref="IPermissionService"/> que tenía antes de F2-07). Cualquier ambigüedad (tenant del token
/// distinto del tenant resuelto para el request, identidad no autenticada) resuelve a
/// <see cref="EffectivePermissions.Empty"/> — default deny, nunca "conceder por las dudas".
/// </summary>
public interface IPermissionEvaluator
{
    Task<EffectivePermissions> EvaluateAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default);
}
