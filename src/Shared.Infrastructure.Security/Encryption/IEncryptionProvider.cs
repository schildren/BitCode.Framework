using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Shared.Infrastructure.Security.Encryption;

/// <summary>
/// Abstracción de cifrado a nivel de aplicación (F2-13, Épica F2-C). El código de negocio cifra/descifra
/// un valor sensible (dato personal, dato financiero sensible, cualquier campo que la política
/// criptográfica -- <c>docs/politica-criptografica.md</c> -- identifique como cifrado en reposo) a través
/// de esta interfaz, nunca invocando <see cref="System.Security.Cryptography"/> directamente ni fijando
/// una clave de cifrado en código o en <c>appsettings.json</c>. El material de clave nunca lo resuelve
/// esta interfaz por sí misma: lo resuelve <see cref="Secrets.ISecretProvider"/> (F2-12) -- el cifrado no
/// reinventa gestión de secretos, la reutiliza.
/// </summary>
public interface IEncryptionProvider
{
    /// <summary>
    /// Cifra <paramref name="plaintext"/> con la clave activa vigente (<see cref="EncryptionOptions.ActiveKeyVersionSecretKey"/>).
    /// El texto cifrado resultante embebe la versión de clave usada -- <see cref="DecryptAsync"/> no
    /// depende de cuál sea la clave activa en el momento de descifrar, solo de que la versión embebida
    /// siga existiendo en <see cref="Secrets.ISecretProvider"/> (rotación sin invalidar datos previos).
    /// </summary>
    Task<Result<string>> EncryptAsync(string plaintext, CancellationToken cancellationToken = default);

    /// <summary>
    /// Descifra un valor producido por <see cref="EncryptAsync"/>. Resuelve la clave por la versión
    /// embebida en <paramref name="ciphertext"/>, no por la clave activa vigente -- por diseño, esto es lo
    /// que permite rotar la clave activa sin dejar de poder descifrar datos cifrados con una clave
    /// anterior (archivada), siempre que esa versión no se haya eliminado del proveedor de secretos.
    /// </summary>
    Task<Result<string>> DecryptAsync(string ciphertext, CancellationToken cancellationToken = default);
}
