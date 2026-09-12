namespace BitCode.Framework.Shared.Infrastructure.Security.Secrets;

/// <summary>Opciones del proveedor de secretos de desarrollo/local (<see cref="ConfigurationSecretProvider"/>).</summary>
public sealed class ConfigurationSecretProviderOptions
{
    public const string SectionName = "Secrets:Configuration";

    /// <summary>
    /// Sub-sección de <see cref="Microsoft.Extensions.Configuration.IConfiguration"/> donde
    /// <see cref="ConfigurationSecretProvider"/> busca cada secreto por clave (<c>"{ValuesSectionPath}:{key}"</c>).
    /// Default <c>"Secrets:Values"</c> — en desarrollo se puebla con <c>dotnet user-secrets set Secrets:Values:MiClave valor</c>
    /// o con la variable de entorno equivalente (<c>Secrets__Values__MiClave</c>), nunca con un valor
    /// literal en <c>appsettings.json</c> versionado.
    /// </summary>
    public string ValuesSectionPath { get; set; } = "Secrets:Values";
}
