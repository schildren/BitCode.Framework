namespace BitCode.Framework.Shared.Infrastructure.Security.Secrets;

/// <summary>
/// Selecciona qué implementación de <see cref="ISecretProvider"/> registra
/// <see cref="SecretProviderServiceCollectionExtensions.AddSharedSecretProvider"/>, exclusivamente por
/// configuración (mismo patrón que <c>OidcOptions</c>/ADR 0004: el código de negocio nunca referencia un
/// proveedor concreto).
/// </summary>
public enum SecretProviderKind
{
    /// <summary>
    /// Lee secretos desde <see cref="Microsoft.Extensions.Configuration.IConfiguration"/> (sección
    /// <see cref="ConfigurationSecretProviderOptions.SectionName"/>) — en desarrollo, esa sección se
    /// puebla vía variables de entorno o <c>dotnet user-secrets</c> (nunca <c>appsettings.json</c>
    /// versionado); ver <see cref="ConfigurationSecretProvider"/>. Es el default cuando no se configura
    /// nada, para no bloquear el trabajo local sin un Vault disponible.
    /// </summary>
    Configuration,

    /// <summary>
    /// Lee secretos de un HashiCorp Vault real (KV v2) vía <see cref="VaultSecretProvider"/>. Ver ADR
    /// 0014 — proveedor propuesto, pendiente de aprobación humana explícita (Plan Maestro sección 13,
    /// "elección de proveedor de secretos/KMS") antes de habilitarse en un entorno productivo real.
    /// </summary>
    Vault,
}

/// <summary>
/// Opciones raíz de F2-12 (sección de configuración <see cref="SectionName"/>). Solo fija qué proveedor
/// usar; cada proveedor tiene su propia sub-sección de opciones (<see cref="ConfigurationSecretProviderOptions"/>,
/// <see cref="VaultSecretProviderOptions"/>).
/// </summary>
public sealed class SecretProviderOptions
{
    public const string SectionName = "Secrets";

    /// <summary>Proveedor activo. Default <see cref="SecretProviderKind.Configuration"/> si no se configura.</summary>
    public SecretProviderKind Provider { get; set; } = SecretProviderKind.Configuration;
}
