using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Security.Encryption;

/// <summary>
/// Registra <see cref="IEncryptionProvider"/> (F2-13, Épica F2-C) sobre <see cref="AesGcmEncryptionProvider"/>
/// -- mismo principio del resto del framework: el código de negocio inyecta <see cref="IEncryptionProvider"/>,
/// nunca el tipo concreto. Requiere que <see cref="Secrets.ISecretProvider"/> ya esté registrado
/// (<c>AddSharedSecretProvider</c>, F2-12) antes de llamar a este método: el cifrado reutiliza el
/// proveedor de secretos configurado para resolver material de clave, no lo reemplaza ni lo duplica.
/// </summary>
public static class EncryptionServiceCollectionExtensions
{
    /// <summary>
    /// Lee la sección <see cref="EncryptionOptions.SectionName"/> (opcional: los defaults de
    /// <see cref="EncryptionOptions"/> ya son válidos sin configuración explícita) y registra
    /// <see cref="IEncryptionProvider"/> como <see cref="AesGcmEncryptionProvider"/>.
    /// </summary>
    public static IServiceCollection AddSharedEncryption(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<EncryptionOptions>(configuration.GetSection(EncryptionOptions.SectionName));
        services.AddScoped<IEncryptionProvider, AesGcmEncryptionProvider>();
        return services;
    }
}
