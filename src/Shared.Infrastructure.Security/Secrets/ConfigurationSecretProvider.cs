using BitCode.Framework.Shared.Kernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.Secrets;

/// <summary>
/// Proveedor de secretos de desarrollo/local (default de <see cref="SecretProviderKind.Configuration"/>,
/// sin dependencia de infraestructura externa): resuelve cada secreto desde
/// <see cref="IConfiguration"/> bajo <see cref="ConfigurationSecretProviderOptions.ValuesSectionPath"/>.
/// En una máquina de desarrollo esa sección se puebla exclusivamente vía <c>dotnet user-secrets</c> o
/// variables de entorno del proceso — nunca vía <c>appsettings.json</c> versionado en el repositorio
/// (ver <c>docs/convenciones.md</c> y el criterio de aceptación de F2-12, "Cero secretos en repositorio").
/// No sustituye a un proveedor real (<see cref="VaultSecretProvider"/>) en producción: existe únicamente
/// para no bloquear el trabajo local ni CI sin un Vault disponible, igual que <c>NullTenantProvider</c>
/// (F1-12) o el JWT propio de <c>Shared.Infrastructure.Security.Jwt</c> son el "camino simple" explícito
/// frente a su contraparte de nivel empresarial.
/// </summary>
public sealed class ConfigurationSecretProvider : ISecretProvider
{
    private readonly IConfiguration _configuration;
    private readonly IOptions<ConfigurationSecretProviderOptions> _options;

    public ConfigurationSecretProvider(IConfiguration configuration, IOptions<ConfigurationSecretProviderOptions> options)
    {
        _configuration = configuration;
        _options = options;
    }

    public Task<Result<string>> GetSecretAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return Task.FromResult(Result.Failure<string>(
                Error.Validation("Secrets.InvalidKey", "La clave del secreto no puede ser vacía.")));
        }

        var sectionPath = _options.Value.ValuesSectionPath;
        var value = _configuration[$"{sectionPath}:{key}"];

        return Task.FromResult(string.IsNullOrEmpty(value)
            ? Result.Failure<string>(Error.NotFound(
                "Secrets.NotFound",
                $"No existe un secreto con clave '{key}' en la sección de configuración '{sectionPath}'."))
            : Result.Success(value));
    }
}
