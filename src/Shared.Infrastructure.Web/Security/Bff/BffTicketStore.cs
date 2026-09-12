using System.Globalization;
using BitCode.Framework.Shared.Infrastructure.Security.Oidc.Bff;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace BitCode.Framework.Shared.Infrastructure.Web.Security.Bff;

/// <summary>
/// Adapta <see cref="IBffSessionStore"/> (Shared.Infrastructure.Security, F2-03) al contrato
/// <see cref="ITicketStore"/> del middleware de autenticación por cookie de ASP.NET Core. Es la pieza
/// que hace que "tokens no quedan expuestos al navegador" (criterio de aceptación de F2-03) sea
/// automático para cualquier endpoint del BFF: al configurar
/// <c>CookieAuthenticationOptions.SessionStore</c> con esta clase, el middleware nunca serializa el
/// <c>AuthenticationTicket</c> (que contiene los tokens vía <c>AuthenticationProperties.StoreTokens</c>)
/// dentro de la cookie -- la cookie recibida por el navegador contiene únicamente el identificador de
/// sesión opaco que <see cref="IBffSessionStore.CreateAsync"/> devuelve.
/// </summary>
internal sealed class BffTicketStore : ITicketStore
{
    private readonly IBffSessionStore _sessionStore;
    private readonly TimeSpan _lifetime;

    public BffTicketStore(IBffSessionStore sessionStore, TimeSpan lifetime)
    {
        _sessionStore = sessionStore;
        _lifetime = lifetime;
    }

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var session = ToSession(ticket);
        return await _sessionStore.CreateAsync(session, _lifetime);
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        // La expiración deslizante del middleware de cookies dispara Renew con un ticket ya actualizado
        // (nueva IssuedUtc/ExpiresUtc) -- se persiste con el MISMO identificador de sesión (la cookie
        // del navegador no cambia durante toda la vida de la sesión).
        var session = ToSession(ticket);
        return _sessionStore.RenewAsync(key, session, _lifetime);
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        var session = await _sessionStore.GetAsync(key);
        return session is null ? null : ToTicket(session);
    }

    public Task RemoveAsync(string key) => _sessionStore.RemoveAsync(key);

    private static BffSession ToSession(AuthenticationTicket ticket)
    {
        var tokens = ticket.Properties.GetTokens()?.ToDictionary(t => t.Name, t => t.Value)
            ?? new Dictionary<string, string>();

        var accessToken = tokens.GetValueOrDefault(BffTokenNames.AccessToken)
            ?? throw new InvalidOperationException(
                $"El ticket de sesión del BFF no incluye un '{BffTokenNames.AccessToken}' -- ¿se llamó a AuthenticationProperties.StoreTokens antes de SignInAsync?");

        DateTimeOffset? expiresAt = tokens.TryGetValue(BffTokenNames.ExpiresAt, out var expiresAtRaw)
            && DateTimeOffset.TryParse(expiresAtRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed
                : null;

        var claims = ticket.Principal.Claims.ToDictionary(c => c.Type, c => c.Value, StringComparer.Ordinal);

        return new BffSession(
            accessToken,
            tokens.GetValueOrDefault(BffTokenNames.RefreshToken),
            tokens.GetValueOrDefault(BffTokenNames.IdToken),
            expiresAt,
            claims);
    }

    private static AuthenticationTicket ToTicket(BffSession session)
    {
        var claims = session.Claims.Select(kv => new System.Security.Claims.Claim(kv.Key, kv.Value));
        var identity = new System.Security.Claims.ClaimsIdentity(claims, BffAuthenticationDefaults.Scheme, "name", "role");
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);

        var properties = new AuthenticationProperties();
        var tokens = new List<AuthenticationToken>
        {
            new() { Name = BffTokenNames.AccessToken, Value = session.AccessToken },
        };
        if (session.RefreshToken is not null)
        {
            tokens.Add(new AuthenticationToken { Name = BffTokenNames.RefreshToken, Value = session.RefreshToken });
        }
        if (session.IdToken is not null)
        {
            tokens.Add(new AuthenticationToken { Name = BffTokenNames.IdToken, Value = session.IdToken });
        }
        if (session.AccessTokenExpiresAtUtc is { } expiresAt)
        {
            tokens.Add(new AuthenticationToken { Name = BffTokenNames.ExpiresAt, Value = expiresAt.ToString("o", CultureInfo.InvariantCulture) });
        }
        properties.StoreTokens(tokens);

        return new AuthenticationTicket(principal, properties, BffAuthenticationDefaults.Scheme);
    }
}
