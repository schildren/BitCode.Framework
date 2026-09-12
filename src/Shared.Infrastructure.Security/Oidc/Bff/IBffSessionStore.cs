namespace BitCode.Framework.Shared.Infrastructure.Security.Oidc.Bff;

/// <summary>
/// Almacén server-side de sesiones BFF (F2-03): guarda los tokens obtenidos de F2-02
/// (<c>IOidcAuthorizationCodeExchanger</c>) fuera del alcance del navegador, indexados por un
/// identificador opaco de alta entropía que es el único valor que llega a la cookie de sesión del
/// cliente. Deliberadamente agnóstico de dónde vive el almacenamiento físico -- la implementación de
/// referencia (<see cref="DistributedCacheBffSessionStore"/>) usa <c>IDistributedCache</c> para que la
/// misma abstracción sirva sin cambios de código tanto en desarrollo (memoria de proceso) como en un
/// despliegue con múltiples instancias (Redis/Valkey, reutilizando <c>Shared.Infrastructure.Caching</c>
/// en vez de introducir un mecanismo de sesión nuevo).
/// </summary>
public interface IBffSessionStore
{
    /// <summary>
    /// Genera un identificador de sesión nuevo, guarda <paramref name="session"/> asociado a él con la
    /// vigencia indicada y lo devuelve. El identificador es el único dato que debe llegar al navegador
    /// (como valor de la cookie de sesión) -- nunca <paramref name="session"/> ni ninguno de sus campos.
    /// </summary>
    Task<string> CreateAsync(BffSession session, TimeSpan lifetime, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reemplaza el contenido de la sesión ya existente identificada por <paramref name="sessionId"/>
    /// (upsert) y renueva su vigencia -- a diferencia de <see cref="CreateAsync"/>, nunca genera un
    /// identificador nuevo. Usado por la expiración deslizante de la cookie de sesión (el navegador
    /// conserva el mismo valor de cookie durante toda la vida de la sesión; solo cambia lo que ese
    /// identificador resuelve server-side).
    /// </summary>
    Task RenewAsync(string sessionId, BffSession session, TimeSpan lifetime, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recupera la sesión asociada a <paramref name="sessionId"/>, o <see langword="null"/> si no
    /// existe (expiró, fue revocada por <see cref="RemoveAsync"/>, o el identificador es inválido/no
    /// corresponde a ninguna sesión emitida por este store).
    /// </summary>
    Task<BffSession?> GetAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Extiende la vigencia de la sesión (expiración deslizante) sin modificar su contenido. No falla
    /// si <paramref name="sessionId"/> ya no existe -- el llamador (p. ej. el middleware de cookies) ya
    /// trata "sesión ausente" como "no autenticado" independientemente de esta llamada.
    /// </summary>
    Task RefreshAsync(string sessionId, TimeSpan lifetime, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revoca la sesión (logout): a partir de esta llamada, <see cref="GetAsync"/> con el mismo
    /// <paramref name="sessionId"/> devuelve <see langword="null"/>. Idempotente -- revocar una sesión
    /// ya ausente no es un error. Es el mecanismo de "revocación por sesión" de F2-06 -- ya cubre tanto
    /// el logout iniciado por el propio usuario (<c>MapSharedBffLogout</c>) como una revocación puntual
    /// de una sesión concreta decidida por otra parte del sistema (p. ej. detección de abuso sobre un
    /// <c>sessionId</c> conocido).
    /// </summary>
    Task RemoveAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revoca de una sola vez TODAS las sesiones activas del sujeto (claim <c>"sub"</c> del id_token,
    /// F2-06) -- el mecanismo detrás de "cerrar sesión en todos los dispositivos" (autoservicio, ver
    /// <c>MapSharedBffLogoutAllDevices</c>) y de una revocación administrativa (p. ej. baja de un
    /// usuario, sospecha de compromiso de credenciales) que necesite invalidar cualquier sesión BFF
    /// vigente de ese sujeto sin conocer de antemano sus <c>sessionId</c> individuales. Idempotente --
    /// revocar un sujeto sin sesiones activas no es un error. La autorización de quién puede invocar esto
    /// para un sujeto distinto del propio (uso administrativo) es responsabilidad del llamador (ver
    /// RBAC/ABAC, Épica F2-B) -- este método es exclusivamente el mecanismo, no la política de acceso.
    /// </summary>
    Task RevokeAllForSubjectAsync(string subject, CancellationToken cancellationToken = default);
}
