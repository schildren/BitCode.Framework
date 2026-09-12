using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace BitCode.Framework.Shared.Infrastructure.Security.Oidc.AuthorizationCode;

/// <summary>
/// Implementación estándar de <see cref="IOidcAuthorizationCodeStateProtector"/> sobre
/// <see cref="IDataProtectionProvider"/> (el mecanismo de cifrado/firma nativo de ASP.NET Core,
/// el mismo que usa el propio middleware OpenIdConnect para su cookie de correlación) -- no se
/// reimplementa cifrado a mano.
/// </summary>
public sealed class OidcAuthorizationCodeStateProtector : IOidcAuthorizationCodeStateProtector
{
    private const string Purpose = "BitCode.Framework.Oidc.AuthorizationCode.CorrelationState.v1";

    private readonly IDataProtector _protector;

    public OidcAuthorizationCodeStateProtector(IDataProtectionProvider dataProtectionProvider)
    {
        _protector = dataProtectionProvider.CreateProtector(Purpose);
    }

    public string Protect(OidcAuthorizationCodeState state)
    {
        var json = JsonSerializer.Serialize(state);
        return _protector.Protect(json);
    }

    public OidcAuthorizationCodeState? Unprotect(string protectedState)
    {
        if (string.IsNullOrWhiteSpace(protectedState))
        {
            return null;
        }

        try
        {
            var json = _protector.Unprotect(protectedState);
            return JsonSerializer.Deserialize<OidcAuthorizationCodeState>(json);
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
