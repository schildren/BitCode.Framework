using BitCode.Framework.Shared.Infrastructure.Security.Encryption;
using BitCode.Framework.Shared.Infrastructure.Security.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Encryption;

/// <summary>
/// F2-13: cifrado a nivel de aplicación sobre AES-256-GCM, con material de clave resuelto vía
/// <see cref="ISecretProvider"/> (F2-12) -- nunca embebido en el propio proveedor de cifrado. El criterio
/// de aceptación explícito de F2-13 ("Rotación y recuperación probadas") se cubre en
/// <see cref="RotatesActiveKey_WithoutBreakingDecryptionOfDataEncryptedBeforeRotation"/> y
/// <see cref="Recovery_ArchivedKeyStillDecryptsData_EvenIfActiveKeyPointerIsLostOrCorrupted"/>.
/// </summary>
public class AesGcmEncryptionProviderTests
{
    private const string ValidKeyV1 = "MTIzNDU2Nzg5MDEyMzQ1Njc4OTAxMjM0NTY3ODkwMTI="; // 32 bytes Base64
    private const string ValidKeyV2 = "QUJDREVGR0hJSktMTU5PUFFSU1RVVldYWVowMTIzNDU="; // otros 32 bytes Base64

    private static AesGcmEncryptionProvider CreateProvider(
        Dictionary<string, string?> secretValues,
        string activeVersion,
        string? activeKeyVersionSecretKey = null,
        string? keyMaterialSecretKeyPrefix = null)
    {
        var values = new Dictionary<string, string?>(secretValues)
        {
            [$"Secrets:Values:{activeKeyVersionSecretKey ?? "Encryption:ActiveKeyVersion"}"] = activeVersion,
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var secretProvider = new ConfigurationSecretProvider(
            configuration,
            Options.Create(new ConfigurationSecretProviderOptions()));

        var options = Options.Create(new EncryptionOptions
        {
            ActiveKeyVersionSecretKey = activeKeyVersionSecretKey ?? "Encryption:ActiveKeyVersion",
            KeyMaterialSecretKeyPrefix = keyMaterialSecretKeyPrefix ?? "Encryption:Keys:",
        });

        return new AesGcmEncryptionProvider(secretProvider, options);
    }

    private static Dictionary<string, string?> WithKeyVersion(string version, string base64Key) => new()
    {
        [$"Secrets:Values:Encryption:Keys:{version}"] = base64Key,
    };

    [Fact]
    public async Task EncryptAsync_ThenDecryptAsync_RoundTripsOriginalPlaintext()
    {
        var provider = CreateProvider(WithKeyVersion("1", ValidKeyV1), activeVersion: "1");

        var encryptResult = await provider.EncryptAsync("dato-sensible-de-prueba");
        encryptResult.IsSuccess.Should().BeTrue();

        var decryptResult = await provider.DecryptAsync(encryptResult.Value);

        decryptResult.IsSuccess.Should().BeTrue();
        decryptResult.Value.Should().Be("dato-sensible-de-prueba");
    }

    [Fact]
    public async Task EncryptAsync_ProducesCiphertextThatEmbedsActiveKeyVersion()
    {
        var provider = CreateProvider(WithKeyVersion("7", ValidKeyV1), activeVersion: "7");

        var result = await provider.EncryptAsync("valor");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().StartWith("v7.");
    }

    [Fact]
    public async Task EncryptAsync_SameValueTwice_ProducesDifferentCiphertexts()
    {
        // El nonce es aleatorio por operación (requisito de seguridad de AES-GCM: nunca reutilizar
        // nonce+clave) -- cifrar el mismo valor dos veces no debe producir el mismo texto cifrado.
        var provider = CreateProvider(WithKeyVersion("1", ValidKeyV1), activeVersion: "1");

        var first = await provider.EncryptAsync("mismo-valor");
        var second = await provider.EncryptAsync("mismo-valor");

        first.Value.Should().NotBe(second.Value);
    }

    [Fact]
    public async Task DecryptAsync_WithTamperedCiphertext_ReturnsFailure()
    {
        var provider = CreateProvider(WithKeyVersion("1", ValidKeyV1), activeVersion: "1");
        var encrypted = (await provider.EncryptAsync("dato-original")).Value;

        // Altera el último carácter del payload Base64 -- simula manipulación en tránsito o en el
        // almacenamiento; AES-GCM debe rechazar el descifrado (tag de autenticación no coincide).
        var tampered = encrypted[..^1] + (encrypted[^1] == 'A' ? 'B' : 'A');

        var result = await provider.DecryptAsync(tampered);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Encryption.TamperedOrCorruptedCiphertext");
    }

    [Fact]
    public async Task DecryptAsync_WithInvalidFormat_ReturnsFailureValidation()
    {
        var provider = CreateProvider(WithKeyVersion("1", ValidKeyV1), activeVersion: "1");

        var result = await provider.DecryptAsync("no-tiene-el-formato-esperado");

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Encryption.InvalidCiphertextFormat");
    }

    [Fact]
    public async Task DecryptAsync_WithEmptyCiphertext_ReturnsFailureValidation()
    {
        var provider = CreateProvider(WithKeyVersion("1", ValidKeyV1), activeVersion: "1");

        var result = await provider.DecryptAsync(string.Empty);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Encryption.InvalidCiphertext");
    }

    [Fact]
    public async Task EncryptAsync_WithoutActiveKeyVersionConfigured_ReturnsFailure()
    {
        var configuration = new ConfigurationBuilder().Build();
        var secretProvider = new ConfigurationSecretProvider(
            configuration, Options.Create(new ConfigurationSecretProviderOptions()));
        var provider = new AesGcmEncryptionProvider(secretProvider, Options.Create(new EncryptionOptions()));

        var result = await provider.EncryptAsync("valor");

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Encryption.ActiveKeyVersionNotConfigured");
    }

    [Fact]
    public async Task EncryptAsync_WithKeyMaterialOfWrongLength_ReturnsFailure()
    {
        var provider = CreateProvider(
            WithKeyVersion("1", Convert.ToBase64String(new byte[16])), // 16 bytes, no 32 (AES-128, no aprobado)
            activeVersion: "1");

        var result = await provider.EncryptAsync("valor");

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Encryption.InvalidKeyMaterial");
    }

    [Fact]
    public async Task DecryptAsync_WithUnknownKeyVersion_ReturnsFailureNotFound()
    {
        var provider = CreateProvider(WithKeyVersion("1", ValidKeyV1), activeVersion: "1");

        var result = await provider.DecryptAsync("v999." + Convert.ToBase64String(new byte[28]));

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Encryption.KeyNotFound");
    }

    [Fact]
    public async Task RotatesActiveKey_WithoutBreakingDecryptionOfDataEncryptedBeforeRotation()
    {
        // Estado antes de rotar: solo existe la versión 1, activa.
        var secretValues = new Dictionary<string, string?>(WithKeyVersion("1", ValidKeyV1))
        {
            ["Secrets:Values:Encryption:ActiveKeyVersion"] = "1",
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(secretValues).Build();
        var secretProvider = new ConfigurationSecretProvider(
            configuration, Options.Create(new ConfigurationSecretProviderOptions()));
        var provider = new AesGcmEncryptionProvider(secretProvider, Options.Create(new EncryptionOptions()));

        var encryptedBeforeRotation = await provider.EncryptAsync("dato-cifrado-antes-de-rotar");
        encryptedBeforeRotation.IsSuccess.Should().BeTrue();
        encryptedBeforeRotation.Value.Should().StartWith("v1.");

        // Rotación: se aprovisiona la versión 2 (sin eliminar la versión 1, que queda archivada) y se
        // mueve el puntero de "versión activa" -- ninguna de las dos operaciones exige reiniciar el
        // proceso ni desplegar código nuevo: es exactamente lo que representa este segundo
        // ConfigurationSecretProvider construido sobre configuración actualizada, simulando el estado del
        // proveedor de secretos después de rotar.
        var secretValuesAfterRotation = new Dictionary<string, string?>(secretValues)
        {
            ["Secrets:Values:Encryption:Keys:2"] = ValidKeyV2,
            ["Secrets:Values:Encryption:ActiveKeyVersion"] = "2",
        };
        var configurationAfterRotation = new ConfigurationBuilder().AddInMemoryCollection(secretValuesAfterRotation).Build();
        var secretProviderAfterRotation = new ConfigurationSecretProvider(
            configurationAfterRotation, Options.Create(new ConfigurationSecretProviderOptions()));
        var providerAfterRotation = new AesGcmEncryptionProvider(
            secretProviderAfterRotation, Options.Create(new EncryptionOptions()));

        // Los datos nuevos usan la clave nueva (v2) -- confirma que la rotación efectivamente tomó efecto.
        var encryptedAfterRotation = await providerAfterRotation.EncryptAsync("dato-cifrado-despues-de-rotar");
        encryptedAfterRotation.IsSuccess.Should().BeTrue();
        encryptedAfterRotation.Value.Should().StartWith("v2.");

        // El dato cifrado ANTES de rotar sigue siendo descifrable después de rotar, sin downtime ni
        // ningún cambio adicional -- este es el criterio de aceptación "rotación... probada".
        var decryptedAfterRotation = await providerAfterRotation.DecryptAsync(encryptedBeforeRotation.Value);
        decryptedAfterRotation.IsSuccess.Should().BeTrue();
        decryptedAfterRotation.Value.Should().Be("dato-cifrado-antes-de-rotar");

        // Y el dato nuevo (v2) también se descifra correctamente.
        var decryptedNewData = await providerAfterRotation.DecryptAsync(encryptedAfterRotation.Value);
        decryptedNewData.IsSuccess.Should().BeTrue();
        decryptedNewData.Value.Should().Be("dato-cifrado-despues-de-rotar");
    }

    [Fact]
    public async Task Recovery_ArchivedKeyStillDecryptsData_EvenIfActiveKeyPointerIsLostOrCorrupted()
    {
        // Se cifra un dato con la versión 1 activa.
        var provider = CreateProvider(WithKeyVersion("1", ValidKeyV1), activeVersion: "1");
        var encrypted = await provider.EncryptAsync("dato-a-recuperar");
        encrypted.IsSuccess.Should().BeTrue();

        // Escenario de recuperación: el puntero de "versión activa" se pierde o queda apuntando a una
        // versión inexistente (ej. un error operativo al rotar, o el propio secreto de puntero se
        // corrompió) -- pero la versión 1, archivada, sigue existiendo en el proveedor de secretos porque
        // la política de rotación no la elimina (ver docs/politica-criptografica.md).
        var secretValuesWithBrokenPointer = new Dictionary<string, string?>(WithKeyVersion("1", ValidKeyV1))
        {
            ["Secrets:Values:Encryption:ActiveKeyVersion"] = "99", // puntero roto/perdido, versión inexistente
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(secretValuesWithBrokenPointer).Build();
        var secretProvider = new ConfigurationSecretProvider(
            configuration, Options.Create(new ConfigurationSecretProviderOptions()));
        var providerWithBrokenPointer = new AesGcmEncryptionProvider(
            secretProvider, Options.Create(new EncryptionOptions()));

        // Cifrar datos NUEVOS falla mientras el puntero esté roto (no hay clave "99") -- esperado.
        var encryptWithBrokenPointer = await providerWithBrokenPointer.EncryptAsync("dato-nuevo");
        encryptWithBrokenPointer.IsSuccess.Should().BeFalse();
        encryptWithBrokenPointer.Error.Code.Should().Be("Encryption.KeyNotFound");

        // Pero el dato cifrado ANTERIORMENTE con la versión 1 se sigue pudiendo recuperar: el descifrado
        // resuelve la clave por la versión embebida en el propio texto cifrado (v1), no por el puntero de
        // versión activa -- la recuperación de datos no depende de que ese puntero esté sano.
        var recovered = await providerWithBrokenPointer.DecryptAsync(encrypted.Value);

        recovered.IsSuccess.Should().BeTrue();
        recovered.Value.Should().Be("dato-a-recuperar");
    }
}
