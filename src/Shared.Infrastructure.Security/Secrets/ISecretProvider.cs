using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Shared.Infrastructure.Security.Secrets;

/// <summary>
/// Abstracción de proveedor de secretos (F2-12, Épica F2-C). El código de aplicación resuelve un
/// secreto (cadena de conexión, client secret de un IdP externo, clave de firma, credencial de un
/// servicio de terceros) a través de esta interfaz — nunca leyendo directamente de
/// <c>appsettings.json</c>, una variable de entorno hardcodeada en el código, o cualquier otro mecanismo
/// que ate el consumidor a un proveedor concreto. Igual que el adapter OIDC/OAuth2 (F2-01, ADR 0004),
/// el proveedor real detrás de esta interfaz es intercambiable exclusivamente por configuración
/// (<see cref="SecretProviderOptions.Provider"/>) — el código de negocio nunca referencia
/// <see cref="Secrets.VaultSecretProvider"/> ni <see cref="Secrets.ConfigurationSecretProvider"/>
/// directamente, solo <see cref="ISecretProvider"/>.
/// </summary>
public interface ISecretProvider
{
    /// <summary>
    /// Resuelve el valor vigente del secreto identificado por <paramref name="key"/>. Un secreto
    /// inexistente es un resultado de negocio esperado (<c>Secrets.NotFound</c>), no una excepción — el
    /// llamador decide si eso es fatal para su caso de uso. Un fallo de comunicación con el proveedor
    /// (Vault caído, credencial de acceso al proveedor inválida) también se modela como
    /// <see cref="Result{TValue}"/> fallido (<c>Secrets.ProviderUnavailable</c>/<c>Secrets.AccessDenied</c>),
    /// nunca como una excepción sin traducir que llegue a <c>GlobalExceptionHandler</c> como un 500 genérico.
    /// </summary>
    Task<Result<string>> GetSecretAsync(string key, CancellationToken cancellationToken = default);
}
