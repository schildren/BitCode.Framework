namespace BitCode.Framework.Shared.Infrastructure.Security.Permissions;

/// <summary>
/// Nombre del claim OAuth2 estándar (RFC 6749 sección 3.3) que transporta los scopes concedidos al
/// token actual, separados por espacio. F2-07 (RBAC 2.0) lo usa como límite adicional — nunca como
/// fuente exclusiva de permisos — sobre los permisos RBAC ya calculados: si el token declara al menos
/// un scope con forma de permiso del framework (<c>"{entidad}.{accion}"</c>, ver
/// <c>docs/convenciones.md</c>), esos scopes actúan como lista explícita de permisos autorizados para
/// ESE token — un permiso que el rol del sujeto tendría en general, pero que no figura entre los
/// scopes declarados, no se concede para esta llamada (por ejemplo, un token de Client Credentials de
/// F2-04 emitido a propósito con un scope reducido). Un scope "de identidad" sin forma de permiso
/// (<c>openid</c>, <c>profile</c>, <c>email</c>, u otro sin punto) se ignora para este propósito: no
/// hay narrowing que aplicar si ningún scope declarado tiene forma de permiso.
/// </summary>
public static class ScopeClaimTypes
{
    public const string Scope = "scope";
}
