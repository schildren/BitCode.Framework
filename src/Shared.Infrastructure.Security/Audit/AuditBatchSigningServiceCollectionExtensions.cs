using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Security.Audit;

/// <summary>
/// Registra <see cref="IAuditBatchSigner"/> (F2-17, Épica F2-D) sobre <see cref="HmacAuditBatchSigner"/> --
/// mismo principio que <see cref="Encryption.EncryptionServiceCollectionExtensions.AddSharedEncryption"/>
/// (F2-13): el código de negocio inyecta <see cref="IAuditBatchSigner"/>, nunca el tipo concreto.
/// Deliberadamente un método de registro SEPARADO de <see cref="AuditServiceCollectionExtensions.AddSharedAuditing"/>
/// -- firmar lotes es opt-in (requiere que un proyecto consumidor ya tenga <see cref="Secrets.ISecretProvider"/>
/// registrado, <c>AddSharedSecretProvider</c>, F2-12, y haya decidido su propia política de "cuándo firmar
/// un lote", fuera de alcance de F2-17) mientras que <c>AddSharedAuditing</c> no requiere ninguna
/// configuración adicional para dejar auditoría básica funcionando.
/// </summary>
public static class AuditBatchSigningServiceCollectionExtensions
{
    /// <summary>
    /// Lee la sección <see cref="AuditBatchSigningOptions.SectionName"/> (opcional: los defaults de
    /// <see cref="AuditBatchSigningOptions"/> ya son válidos sin configuración explícita) y registra
    /// <see cref="IAuditBatchSigner"/> como <see cref="HmacAuditBatchSigner"/>. Requiere que <see
    /// cref="Secrets.ISecretProvider"/> ya esté registrado antes de llamar a este método.
    /// </summary>
    public static IServiceCollection AddSharedAuditBatchSigning(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AuditBatchSigningOptions>(configuration.GetSection(AuditBatchSigningOptions.SectionName));
        services.AddScoped<IAuditBatchSigner, HmacAuditBatchSigner>();
        return services;
    }
}
