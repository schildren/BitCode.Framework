using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Infrastructure.Security.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Audit;

/// <summary>
/// F2-17: firma de lotes de auditoría sobre HMAC-SHA256, con material de clave resuelto vía <see
/// cref="ISecretProvider"/> (F2-12) -- mismo criterio de prueba que <c>AesGcmEncryptionProviderTests</c>
/// (F2-13): se ejercita <see cref="HmacAuditBatchSigner"/> real contra un <see cref="ConfigurationSecretProvider"/>
/// real, sin mockear el propio mecanismo criptográfico bajo prueba. El criterio de aceptación explícito de
/// F2-17 ("Verificación independiente") se cubre firmando y verificando con instancias distintas de <see
/// cref="HmacAuditBatchSigner"/> (simulando procesos/servicios distintos), y el hueco documentado de F2-16
/// ("no protege contra una reconstrucción completa y consistente de la cadena de hashes") se cubre en
/// <see cref="VerifyAsync_WhenAttackerRebuildsConsistentAlternateChain_SignatureStillInvalid"/>.
/// </summary>
public class HmacAuditBatchSignerTests
{
    private const string ValidKeyV1 = "MTIzNDU2Nzg5MDEyMzQ1Njc4OTAxMjM0NTY3ODkwMTI="; // 32 bytes Base64
    private const string ValidKeyV2 = "QUJDREVGR0hJSktMTU5PUFFSU1RVVldYWVowMTIzNDU="; // otros 32 bytes Base64
    private const string OtherValidKey = "WlZYVVRTUlFQT05NTEtKSUhHRkVEQ0JBOTg3NjU0MzI="; // 32 bytes distintos

    private static HmacAuditBatchSigner CreateSigner(
        Dictionary<string, string?> secretValues,
        string activeVersion,
        DateTime? now = null)
    {
        var values = new Dictionary<string, string?>(secretValues)
        {
            ["Secrets:Values:AuditBatchSigning:ActiveKeyVersion"] = activeVersion,
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var secretProvider = new ConfigurationSecretProvider(
            configuration, Options.Create(new ConfigurationSecretProviderOptions()));

        var timeProvider = now.HasValue
            ? new FakeTimeProvider(now.Value)
            : null;

        return new HmacAuditBatchSigner(secretProvider, Options.Create(new AuditBatchSigningOptions()), timeProvider);
    }

    private static Dictionary<string, string?> WithKeyVersion(string version, string base64Key) => new()
    {
        [$"Secrets:Values:AuditBatchSigning:Keys:{version}"] = base64Key,
    };

    private static IReadOnlyList<AuditEntry> CreateBatch(int count, Guid? tenantId = null)
    {
        var writer = new InMemoryAuditWriter();
        for (var i = 0; i < count; i++)
        {
            writer.WriteAsync(new AuditEntryRequest(
                actor: new AuditActor($"user-{i}", AuditActorType.User),
                tenantId: tenantId,
                action: "pedidos.eliminar",
                resource: new AuditResource("pedidos", $"pedido-{i}"),
                outcome: AuditOutcome.Success))
                .GetAwaiter().GetResult();
        }

        return writer.Entries;
    }

    [Fact]
    public async Task SignAsync_ThenVerifyAsync_WithDistinctSignerInstance_ReturnsValidTrue()
    {
        // Firma y verificación con dos INSTANCIAS distintas (simula un servicio que firma y otro proceso
        // de verificación distinto, ambos dentro del mismo perímetro de confianza/proveedor de secretos) --
        // "verificación independiente" del criterio de aceptación de F2-17.
        var secretValues = WithKeyVersion("1", ValidKeyV1);
        var signer = CreateSigner(secretValues, activeVersion: "1");
        var verifier = CreateSigner(secretValues, activeVersion: "1");
        var batch = CreateBatch(3);

        var signResult = await signer.SignAsync(batch);
        signResult.IsSuccess.Should().BeTrue();

        var verifyResult = await verifier.VerifyAsync(batch, signResult.Value);

        verifyResult.IsSuccess.Should().BeTrue();
        verifyResult.Value.Should().BeTrue();
    }

    [Fact]
    public async Task SignAsync_EmptyBatch_ReturnsFailure()
    {
        var signer = CreateSigner(WithKeyVersion("1", ValidKeyV1), activeVersion: "1");

        var result = await signer.SignAsync(Array.Empty<AuditEntry>());

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("AuditBatchSigning.EmptyBatch");
    }

    [Fact]
    public async Task VerifyAsync_EmptyBatch_ReturnsFailure()
    {
        var signer = CreateSigner(WithKeyVersion("1", ValidKeyV1), activeVersion: "1");
        var batch = CreateBatch(1);
        var signature = (await signer.SignAsync(batch)).Value;

        var result = await signer.VerifyAsync(Array.Empty<AuditEntry>(), signature);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("AuditBatchSigning.EmptyBatch");
    }

    [Fact]
    public async Task VerifyAsync_AfterAlteringAnyFieldOfAnyEntry_ReturnsFalse()
    {
        var signer = CreateSigner(WithKeyVersion("1", ValidKeyV1), activeVersion: "1");
        var batch = CreateBatch(3);
        var signature = (await signer.SignAsync(batch)).Value;

        // Altera un campo cualquiera de un registro cualquiera DESPUÉS de firmar (simula manipulación
        // directa del almacenamiento subyacente que no recalcula ningún hash) -- el AuditHash de ese
        // registro deja de coincidir con lo firmado.
        var tamperedEntry = new AuditEntry(
            batch[1].Id, batch[1].OccurredAtUtc, batch[1].Actor, batch[1].TenantId, "pedidos.eliminar",
            new AuditResource("pedidos", "pedido-manipulado"), batch[1].Outcome, batch[1].Reason,
            batch[1].CorrelationId, batch[1].TraceId, batch[1].IpAddress, batch[1].Metadata,
            batch[1].AuditHash, batch[1].PreviousAuditHash);
        var tamperedBatch = batch.ToArray();
        tamperedBatch[1] = tamperedEntry;

        var result = await signer.VerifyAsync(tamperedBatch, signature);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyAsync_WhenAttackerRebuildsConsistentAlternateChain_SignatureStillInvalid()
    {
        // El hueco explícito de F2-16: un atacante con acceso de ESCRITURA al almacenamiento puede alterar
        // un registro y RECALCULAR de forma consistente el AuditHash de ese registro y el PreviousAuditHash
        // de todos los que lo siguen, con el mismo algoritmo público (AuditHashCalculator) -- esa cadena
        // alternativa pasaría IAuditIntegrityVerifier.Verify sin detectar nada. La firma de F2-17 sigue
        // detectando la manipulación porque el atacante no tiene la clave de firma.
        var signer = CreateSigner(WithKeyVersion("1", ValidKeyV1), activeVersion: "1");
        var originalBatch = CreateBatch(3);
        var signature = (await signer.SignAsync(originalBatch)).Value;

        var alternateChain = new AuditEntry[originalBatch.Count];
        string? previousHash = null;
        for (var i = 0; i < originalBatch.Count; i++)
        {
            var original = originalBatch[i];
            var resource = i == 1
                ? new AuditResource("pedidos", "pedido-manipulado") // el cambio real del atacante
                : original.Resource;

            var recomputedHash = AuditHashCalculator.Compute(
                original.Id, original.OccurredAtUtc, original.Actor, original.TenantId, original.Action,
                resource, original.Outcome, original.Reason, original.CorrelationId, original.TraceId,
                original.IpAddress, original.Metadata);

            alternateChain[i] = new AuditEntry(
                original.Id, original.OccurredAtUtc, original.Actor, original.TenantId, original.Action,
                resource, original.Outcome, original.Reason, original.CorrelationId, original.TraceId,
                original.IpAddress, original.Metadata, recomputedHash, previousHash);

            previousHash = recomputedHash;
        }

        // La cadena alternativa es internamente consistente (F2-16 no la distinguiría de la original).
        var integrityResult = new AuditIntegrityVerifier().Verify(alternateChain);
        integrityResult.IsValid.Should().BeTrue();

        // Pero la firma de F2-17, emitida sobre el contenido ORIGINAL, no valida contra la cadena alternativa.
        var signatureResult = await signer.VerifyAsync(alternateChain, signature);

        signatureResult.IsSuccess.Should().BeTrue();
        signatureResult.Value.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyAsync_WithReorderedBatch_ReturnsFalse()
    {
        var signer = CreateSigner(WithKeyVersion("1", ValidKeyV1), activeVersion: "1");
        var batch = CreateBatch(3);
        var signature = (await signer.SignAsync(batch)).Value;

        var reordered = new[] { batch[1], batch[0], batch[2] };

        var result = await signer.VerifyAsync(reordered, signature);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyAsync_WithTamperedSignatureValue_ReturnsFalse()
    {
        var signer = CreateSigner(WithKeyVersion("1", ValidKeyV1), activeVersion: "1");
        var batch = CreateBatch(2);
        var original = (await signer.SignAsync(batch)).Value;
        var tampered = new AuditBatchSignature(original.KeyVersion, original.SignedAtUtc, Convert.ToBase64String([1, 2, 3, 4]));

        var result = await signer.VerifyAsync(batch, tampered);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyAsync_WithNonBase64SignatureValue_ReturnsFalseWithoutThrowing()
    {
        var signer = CreateSigner(WithKeyVersion("1", ValidKeyV1), activeVersion: "1");
        var batch = CreateBatch(1);
        var original = (await signer.SignAsync(batch)).Value;
        var tampered = new AuditBatchSignature(original.KeyVersion, original.SignedAtUtc, "no-es-base64-!!");

        var result = await signer.VerifyAsync(batch, tampered);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyAsync_WithWrongKeyMaterialForSameVersion_ReturnsFalse()
    {
        // Simula una clave de verificación incorrecta: el verificador resuelve la misma VERSIÓN embebida en
        // la firma, pero el proveedor de secretos que consulta tiene un material distinto para esa versión
        // (por ejemplo, un entorno mal configurado) -- la firma no puede validar contra una clave distinta a
        // la usada para firmar.
        var signer = CreateSigner(WithKeyVersion("1", ValidKeyV1), activeVersion: "1");
        var verifierWithWrongKey = CreateSigner(WithKeyVersion("1", OtherValidKey), activeVersion: "1");
        var batch = CreateBatch(2);

        var signature = (await signer.SignAsync(batch)).Value;
        var result = await verifierWithWrongKey.VerifyAsync(batch, signature);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyAsync_AfterRotatingActiveKey_StillValidatesSignatureFromPreviousVersion()
    {
        // Rotación: la firma embebe la versión usada (KeyVersion), no depende de cuál sea la versión activa
        // en el momento de verificar -- mismo criterio de rotación que AesGcmEncryptionProvider (F2-13).
        var secretValues = WithKeyVersion("1", ValidKeyV1);
        var signer = CreateSigner(secretValues, activeVersion: "1");
        var batch = CreateBatch(2);
        var signature = (await signer.SignAsync(batch)).Value;
        signature.KeyVersion.Should().Be("1");

        var secretValuesAfterRotation = new Dictionary<string, string?>(secretValues)
        {
            ["Secrets:Values:AuditBatchSigning:Keys:2"] = ValidKeyV2,
        };
        var verifierAfterRotation = CreateSigner(secretValuesAfterRotation, activeVersion: "2");

        var result = await verifierAfterRotation.VerifyAsync(batch, signature);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task SignAsync_WithoutActiveKeyVersionConfigured_ReturnsFailure()
    {
        var configuration = new ConfigurationBuilder().Build();
        var secretProvider = new ConfigurationSecretProvider(
            configuration, Options.Create(new ConfigurationSecretProviderOptions()));
        var signer = new HmacAuditBatchSigner(secretProvider, Options.Create(new AuditBatchSigningOptions()));
        var batch = CreateBatch(1);

        var result = await signer.SignAsync(batch);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("AuditBatchSigning.ActiveKeyVersionNotConfigured");
    }

    [Fact]
    public async Task VerifyAsync_WithUnknownKeyVersionInSignature_ReturnsFailureNotFound()
    {
        var signer = CreateSigner(WithKeyVersion("1", ValidKeyV1), activeVersion: "1");
        var batch = CreateBatch(1);
        var signatureWithUnknownVersion = new AuditBatchSignature("999", DateTime.UtcNow, Convert.ToBase64String([1, 2, 3]));

        var result = await signer.VerifyAsync(batch, signatureWithUnknownVersion);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("AuditBatchSigning.KeyNotFound");
    }

    [Fact]
    public async Task SignAsync_WithKeyMaterialShorterThanMinimum_ReturnsFailure()
    {
        var signer = CreateSigner(
            WithKeyVersion("1", Convert.ToBase64String(new byte[16])), activeVersion: "1");
        var batch = CreateBatch(1);

        var result = await signer.SignAsync(batch);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("AuditBatchSigning.InvalidKeyMaterial");
    }

    [Fact]
    public async Task TwoDifferentBatchesSignedAtSameInstant_ProduceDifferentSignatures()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var signer = CreateSigner(WithKeyVersion("1", ValidKeyV1), activeVersion: "1", now);

        var batchA = CreateBatch(2);
        var batchB = CreateBatch(2);

        var signatureA = (await signer.SignAsync(batchA)).Value;
        var signatureB = (await signer.SignAsync(batchB)).Value;

        signatureA.Value.Should().NotBe(signatureB.Value);
    }

    [Fact]
    public void AuditBatchSignature_ToString_ThenTryParse_RoundTrips()
    {
        var original = new AuditBatchSignature("7", new DateTime(2026, 5, 1, 12, 30, 0, DateTimeKind.Utc), "Zm9v");

        var serialized = original.ToString();
        var parsed = AuditBatchSignature.TryParse(serialized, out var reconstructed);

        parsed.Should().BeTrue();
        reconstructed.Should().NotBeNull();
        reconstructed!.KeyVersion.Should().Be(original.KeyVersion);
        reconstructed.SignedAtUtc.Should().Be(original.SignedAtUtc);
        reconstructed.Value.Should().Be(original.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-tiene-formato-esperado")]
    [InlineData("v1.no-es-fecha.Zm9v")]
    [InlineData("v.2026-01-01T00:00:00Z.Zm9v")]
    public void AuditBatchSignature_TryParse_WithInvalidFormat_ReturnsFalse(string invalid)
    {
        var parsed = AuditBatchSignature.TryParse(invalid, out var reconstructed);

        parsed.Should().BeFalse();
        reconstructed.Should().BeNull();
    }

    private sealed class FakeTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc));
    }
}
