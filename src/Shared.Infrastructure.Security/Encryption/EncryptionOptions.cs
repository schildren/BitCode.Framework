namespace BitCode.Framework.Shared.Infrastructure.Security.Encryption;

/// <summary>
/// Opciones de F2-13 (sección de configuración <see cref="SectionName"/>). No contienen ninguna clave de
/// cifrado: solo las claves lógicas de <see cref="Secrets.ISecretProvider"/> (F2-12) donde
/// <see cref="AesGcmEncryptionProvider"/> resuelve la versión activa y el material de cada versión de
/// clave -- el material en sí vive exclusivamente en el proveedor de secretos configurado (Vault en
/// producción, <c>ConfigurationSecretProvider</c>/user-secrets en desarrollo), nunca en
/// <c>appsettings.json</c> versionado.
/// </summary>
public sealed class EncryptionOptions
{
    public const string SectionName = "Encryption";

    /// <summary>
    /// Clave lógica que <see cref="Secrets.ISecretProvider"/> resuelve para obtener la versión de clave
    /// activa (por ejemplo <c>"1"</c>, <c>"2"</c>...), usada para cifrar datos nuevos. Rotar la clave
    /// activa es aprovisionar una nueva versión de material de clave y cambiar el valor de este secreto
    /// -- ninguna de las dos operaciones requiere desplegar código ni interrumpir el servicio.
    /// </summary>
    public string ActiveKeyVersionSecretKey { get; set; } = "Encryption:ActiveKeyVersion";

    /// <summary>
    /// Prefijo de la clave lógica que <see cref="Secrets.ISecretProvider"/> resuelve para obtener el
    /// material (AES-256, 32 bytes, Base64) de una versión de clave concreta -- la clave efectiva
    /// resuelta es <c>"{KeyMaterialSecretKeyPrefix}{version}"</c>. Las versiones anteriores a la activa se
    /// conservan indefinidamente en el proveedor de secretos (nunca se eliminan salvo "crypto shredding"
    /// deliberado -- ver <c>docs/politica-criptografica.md</c>) para poder seguir descifrando datos
    /// cifrados con ellas.
    /// </summary>
    public string KeyMaterialSecretKeyPrefix { get; set; } = "Encryption:Keys:";
}
