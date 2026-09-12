using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;

namespace BitCode.Framework.Shared.Infrastructure.Security.Oidc.Bff;

/// <summary>
/// Implementación de referencia de <see cref="IBffSessionStore"/> sobre <c>IDistributedCache</c> --
/// deliberadamente la misma abstracción que ya usa el resto del framework para estado compartido entre
/// instancias (<c>Shared.Infrastructure.Caching</c>, F1-16/F1-25): en desarrollo, sin Redis configurado,
/// funciona con la caché distribuida en memoria de proceso (<c>AddDistributedMemoryCache</c>,
/// registrada como fallback por <see cref="BffSessionServiceCollectionExtensions.AddSharedBffSessionStore"/>
/// si ningún otro <c>IDistributedCache</c> fue registrado antes); en producción con múltiples instancias,
/// basta con que el proyecto consumidor llame a <c>AddSharedCaching</c> con Redis configurado -- esta
/// clase no necesita ningún cambio de código para pasar de una topología a la otra.
/// </summary>
/// <remarks>
/// El valor persistido se cifra con <see cref="IDataProtectionProvider"/> antes de escribirlo en la
/// caché distribuida: aunque <c>IDistributedCache</c> no es la fuente de verdad de ledger/saldos (esto
/// es exclusivamente una sesión de aplicación, no dato de negocio), sí contiene tokens sensibles, y un
/// operador de Redis/Valkey compartido no debería poder leerlos en texto plano.
/// </remarks>
public sealed class DistributedCacheBffSessionStore : IBffSessionStore
{
    private const string Purpose = "BitCode.Framework.Oidc.Bff.Session.v1";
    private const string SubjectIndexPurpose = "BitCode.Framework.Oidc.Bff.SessionSubjectIndex.v1";
    private const string KeyPrefix = "bc-bff-session:";
    private const string SubjectIndexKeyPrefix = "bc-bff-session-subject-index:";
    private const string SubjectClaimType = "sub";

    private readonly IDistributedCache _cache;
    private readonly IDataProtector _protector;
    private readonly IDataProtector _subjectIndexProtector;

    public DistributedCacheBffSessionStore(IDistributedCache cache, IDataProtectionProvider dataProtectionProvider)
    {
        _cache = cache;
        _protector = dataProtectionProvider.CreateProtector(Purpose);
        _subjectIndexProtector = dataProtectionProvider.CreateProtector(SubjectIndexPurpose);
    }

    public async Task<string> CreateAsync(BffSession session, TimeSpan lifetime, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        // 256 bits de entropía, Base64Url -- ni predecible ni reutilizable entre sesiones (mismo
        // estándar de "identificador de sesión opaco" que el "state"/"nonce" de F2-02).
        var sessionId = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        await WriteAsync(sessionId, session, lifetime, cancellationToken);
        await AddToSubjectIndexAsync(session, sessionId, lifetime, cancellationToken);
        return sessionId;
    }

    public async Task<BffSession?> GetAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        var protectedBytes = await _cache.GetAsync(KeyPrefix + sessionId, cancellationToken);
        if (protectedBytes is null)
        {
            return null;
        }

        try
        {
            var jsonBytes = _protector.Unprotect(protectedBytes);
            return JsonSerializer.Deserialize<BffSession>(jsonBytes);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Cache tamperada, o cifrada con otra clave de Data Protection (rotación de claves, otro
            // despliegue) -- se trata igual que "sesión ausente", nunca se propaga como excepción.
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task RenewAsync(string sessionId, BffSession session, TimeSpan lifetime, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(session);
        await WriteAsync(sessionId, session, lifetime, cancellationToken);
        await AddToSubjectIndexAsync(session, sessionId, lifetime, cancellationToken);
    }

    public Task RefreshAsync(string sessionId, TimeSpan lifetime, CancellationToken cancellationToken = default) =>
        _cache.RefreshAsync(KeyPrefix + sessionId, cancellationToken);

    public async Task RemoveAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        // Best-effort: se lee la sesión ANTES de borrarla únicamente para poder quitarla también del
        // índice por sujeto (evita que el índice acumule identificadores de sesiones ya revocadas). Si
        // la sesión ya no existe, o el índice ya no la contiene, no es un error -- ver comentario de
        // RevokeAllForSubjectAsync sobre por qué el índice es, a propósito, best-effort y no la fuente
        // de verdad de qué sesiones siguen vigentes (eso sigue siendo GetAsync).
        var session = await GetAsync(sessionId, cancellationToken);
        if (session is not null)
        {
            await RemoveFromSubjectIndexAsync(session, sessionId, cancellationToken);
        }

        await _cache.RemoveAsync(KeyPrefix + sessionId, cancellationToken);
    }

    public async Task RevokeAllForSubjectAsync(string subject, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        var indexKey = SubjectIndexKeyPrefix + subject;
        var sessionIds = await ReadSubjectIndexAsync(indexKey, cancellationToken);

        foreach (var sessionId in sessionIds)
        {
            // Se revoca directamente por clave -- releer cada sesión para actualizar el índice sería
            // trabajo redundante: el índice completo se borra al final de este método de todas formas.
            await _cache.RemoveAsync(KeyPrefix + sessionId, cancellationToken);
        }

        await _cache.RemoveAsync(indexKey, cancellationToken);
    }

    private async Task WriteAsync(string sessionId, BffSession session, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(session);
        var protectedBytes = _protector.Protect(jsonBytes);
        await _cache.SetAsync(
            KeyPrefix + sessionId,
            protectedBytes,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = lifetime },
            cancellationToken);
    }

    /// <remarks>
    /// El índice por sujeto (F2-06) es deliberadamente best-effort, no transaccional: se implementa como
    /// "leer array JSON, agregar/quitar un elemento, reescribir" sobre <c>IDistributedCache</c> (que no
    /// ofrece operaciones atómicas de conjunto ni CAS). En el peor caso -- dos logins concurrentes del
    /// mismo sujeto en la misma ventana de milisegundos, en instancias distintas -- una de las dos
    /// escrituras del índice puede perderse (uno de los dos <c>sessionId</c> queda fuera del índice). Eso
    /// nunca compromete la revocación por <c>sessionId</c> individual (<see cref="RemoveAsync"/>, la
    /// fuente de verdad real de "¿esta sesión sigue vigente?" es siempre <see cref="GetAsync"/> contra la
    /// clave de sesión, nunca el índice) -- el único efecto de una entrada perdida en el índice es que
    /// <see cref="RevokeAllForSubjectAsync"/> podría no alcanzar esa sesión puntual, que seguirá
    /// existiendo hasta su expiración natural. Aceptable para el caso de uso ("cerrar sesión en todos los
    /// dispositivos", revocación administrativa best-effort) -- si en el futuro se necesita una garantía
    /// más fuerte, la vía es una estructura de datos con operaciones atómicas (p. ej. un SET de Redis),
    /// no esta clase.
    /// </remarks>
    private async Task AddToSubjectIndexAsync(BffSession session, string sessionId, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        if (!TryGetSubject(session, out var subject))
        {
            return;
        }

        var indexKey = SubjectIndexKeyPrefix + subject;
        var sessionIds = await ReadSubjectIndexAsync(indexKey, cancellationToken);
        if (!sessionIds.Contains(sessionId, StringComparer.Ordinal))
        {
            sessionIds.Add(sessionId);
        }

        await WriteSubjectIndexAsync(indexKey, sessionIds, lifetime, cancellationToken);
    }

    private async Task RemoveFromSubjectIndexAsync(BffSession session, string sessionId, CancellationToken cancellationToken)
    {
        if (!TryGetSubject(session, out var subject))
        {
            return;
        }

        var indexKey = SubjectIndexKeyPrefix + subject;
        var sessionIds = await ReadSubjectIndexAsync(indexKey, cancellationToken);
        if (sessionIds.RemoveAll(id => string.Equals(id, sessionId, StringComparison.Ordinal)) == 0)
        {
            return;
        }

        if (sessionIds.Count == 0)
        {
            await _cache.RemoveAsync(indexKey, cancellationToken);
            return;
        }

        // La entrada del índice ya existía (tenía un lifetime propio); al reescribirla tras un logout
        // individual no se conoce cuánto faltaba para esa expiración original, así que se conserva una
        // vigencia larga conservadora en vez de arriesgar a acortarla -- de todas formas nunca es la
        // fuente de verdad (ver comentario de AddToSubjectIndexAsync).
        await WriteSubjectIndexAsync(indexKey, sessionIds, TimeSpan.FromDays(1), cancellationToken);
    }

    private async Task<List<string>> ReadSubjectIndexAsync(string indexKey, CancellationToken cancellationToken)
    {
        var protectedBytes = await _cache.GetAsync(indexKey, cancellationToken);
        if (protectedBytes is null)
        {
            return new List<string>();
        }

        try
        {
            var jsonBytes = _subjectIndexProtector.Unprotect(protectedBytes);
            return JsonSerializer.Deserialize<List<string>>(jsonBytes) ?? new List<string>();
        }
        catch (CryptographicException)
        {
            return new List<string>();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }

    private async Task WriteSubjectIndexAsync(string indexKey, List<string> sessionIds, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(sessionIds);
        var protectedBytes = _subjectIndexProtector.Protect(jsonBytes);
        await _cache.SetAsync(
            indexKey,
            protectedBytes,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = lifetime },
            cancellationToken);
    }

    private static bool TryGetSubject(BffSession session, out string subject)
    {
        if (session.Claims.TryGetValue(SubjectClaimType, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            subject = value;
            return true;
        }

        subject = string.Empty;
        return false;
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
