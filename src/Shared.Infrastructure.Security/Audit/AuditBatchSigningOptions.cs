namespace BitCode.Framework.Shared.Infrastructure.Security.Audit;

/// <summary>
/// Opciones de F2-17 (sección de configuración <see cref="SectionName"/>). Mismo patrón que
/// <see cref="Encryption.EncryptionOptions"/> (F2-13): no contienen ninguna clave de firma en sí, solo las
/// claves lógicas de <see cref="Secrets.ISecretProvider"/> (F2-12) donde <see cref="HmacAuditBatchSigner"/>
/// resuelve la versión activa y el material de cada versión de clave de firma -- el material en sí vive
/// exclusivamente en el proveedor de secretos configurado, nunca en <c>appsettings.json</c> versionado.
/// <para>
/// Deliberadamente una sección de configuración/clave lógica separada de <see cref="Encryption.EncryptionOptions"/>:
/// la clave que firma lotes de auditoría y la clave que cifra datos de aplicación son materiales
/// criptográficos independientes con propósitos distintos (integridad de auditoría vs. confidencialidad de
/// datos) -- comprometer una no debe comprometer la otra, y cada una rota con su propio ciclo de vida.
/// </para>
/// </summary>
public sealed class AuditBatchSigningOptions
{
    public const string SectionName = "AuditBatchSigning";

    /// <summary>
    /// Clave lógica que <see cref="Secrets.ISecretProvider"/> resuelve para obtener la versión de clave de
    /// firma activa (por ejemplo <c>"1"</c>, <c>"2"</c>...), usada para firmar lotes nuevos. Rotar la clave
    /// activa es aprovisionar una nueva versión de material de clave y cambiar el valor de este secreto --
    /// las firmas ya emitidas con una versión anterior se siguen verificando (la versión usada para firmar
    /// va embebida en <see cref="AuditBatchSignature.KeyVersion"/>, igual que <see
    /// cref="Encryption.AesGcmEncryptionProvider"/> con la versión de clave de cifrado).
    /// </summary>
    public string ActiveKeyVersionSecretKey { get; set; } = "AuditBatchSigning:ActiveKeyVersion";

    /// <summary>
    /// Prefijo de la clave lógica que <see cref="Secrets.ISecretProvider"/> resuelve para obtener el
    /// material (mínimo 32 bytes, Base64) de una versión de clave de firma concreta -- la clave efectiva
    /// resuelta es <c>"{KeyMaterialSecretKeyPrefix}{version}"</c>. Las versiones anteriores a la activa se
    /// conservan indefinidamente en el proveedor de secretos mientras existan lotes firmados con ellas que
    /// todavía deban poder verificarse.
    /// </summary>
    public string KeyMaterialSecretKeyPrefix { get; set; } = "AuditBatchSigning:Keys:";
}
