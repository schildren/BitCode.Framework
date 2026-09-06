namespace BitCode.Framework.Shared.Infrastructure.Web.Security.Bff;

/// <summary>
/// Nombre del esquema de autenticación por cookie que sostiene la sesión server-side del BFF (F2-03).
/// Deliberadamente distinto de <c>"Bearer"</c> (usado por <c>AddSharedOidcAuthentication</c>,
/// Shared.Infrastructure.Security -- validación de tokens en APIs) y del JWT propio: un mismo proceso
/// puede alojar el BFF (cookie) y, si corresponde, validar tokens Bearer en otras rutas, sin que ambos
/// esquemas colisionen.
/// </summary>
public static class BffAuthenticationDefaults
{
    public const string Scheme = "BitCode.Bff";
}
