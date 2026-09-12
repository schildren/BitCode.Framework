namespace BitCode.Framework.Shared.Infrastructure.Security.Oidc.AuthorizationCode;

/// <summary>
/// Protege (cifra + firma) el <see cref="OidcAuthorizationCodeState"/> antes de guardarlo en la cookie
/// de correlación HttpOnly entre <c>/auth/login</c> y <c>/auth/callback</c> -- ni la SPA (que no puede
/// leer una cookie HttpOnly) ni un atacante que la intercepte pueden leer el <c>code_verifier</c> ni
/// falsificar el <c>state</c> sin la clave de protección de datos del servidor.
/// </summary>
public interface IOidcAuthorizationCodeStateProtector
{
    string Protect(OidcAuthorizationCodeState state);

    /// <summary>
    /// Revierte <see cref="Protect"/>. Devuelve <c>null</c> -- nunca lanza -- si el valor está
    /// ausente, corrupto, expiró (según la vigencia de las claves de Data Protection) o fue alterado:
    /// el callback debe tratar cualquiera de esos casos como "correlación inválida", igual que un
    /// <c>state</c> que no matchea.
    /// </summary>
    OidcAuthorizationCodeState? Unprotect(string protectedState);
}
