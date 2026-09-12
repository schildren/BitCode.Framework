using System.Text;
using BitCode.Framework.Shared.Infrastructure.Security.Audit.Worm;
using BitCode.Framework.Shared.Kernel;
using FluentAssertions;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Audit.Worm;

/// <summary>
/// F2-18 (Épica F2-D, entregable "Pipeline de retención", criterio de aceptación "Escritura y lectura
/// probadas"): verifica que <see cref="InMemoryWormStorage"/> modela correctamente la semántica WORM
/// documentada en <see cref="IWormStorage"/> -- este es el placeholder de desarrollo, EXACTAMENTE con el
/// mismo criterio que <see cref="InMemoryAuditWriter"/> (F2-15), así que lo que se prueba acá es que la
/// semántica de negocio (write-once, retención bloquea eliminación prematura, eliminación después de
/// expirar sí es válida) se cumple, no que sea una implementación productiva.
/// </summary>
public class InMemoryWormStorageTests
{
    private static byte[] Content(string text) => Encoding.UTF8.GetBytes(text);

    [Fact]
    public async Task WriteAsync_ThenReadAsync_DevuelveElContenidoIntegro()
    {
        var storage = new InMemoryWormStorage();
        var content = Content("lote de auditoria exportado");

        var writeResult = await storage.WriteAsync(new WormWriteRequest("tenant/2026/lote-1", content, TimeSpan.FromDays(30)));
        writeResult.IsSuccess.Should().BeTrue();

        var readResult = await storage.ReadAsync("tenant/2026/lote-1");

        readResult.IsSuccess.Should().BeTrue();
        readResult.Value.Content.Should().Equal(content);
        readResult.Value.Metadata.Key.Should().Be("tenant/2026/lote-1");
        readResult.Value.Metadata.ContentHash.Should().Be(writeResult.Value.ContentHash);
    }

    [Fact]
    public async Task WriteAsync_ConClaveYaExistente_Falla_NoSobrescribe()
    {
        var storage = new InMemoryWormStorage();
        await storage.WriteAsync(new WormWriteRequest("k1", Content("original"), TimeSpan.FromDays(1)));

        var secondWrite = await storage.WriteAsync(new WormWriteRequest("k1", Content("intento de alterar"), TimeSpan.FromDays(1)));

        secondWrite.IsFailure.Should().BeTrue();
        secondWrite.Error.Code.Should().Be("Worm.ObjectAlreadyExists");
        secondWrite.Error.Type.Should().Be(ErrorType.Conflict);

        // El contenido original sigue intacto -- la escritura fallida no lo tocó.
        var read = await storage.ReadAsync("k1");
        read.Value.Content.Should().Equal(Content("original"));
    }

    [Fact]
    public async Task DeleteAsync_AntesDeExpirarLaRetencion_Falla_ComoErrorDeNegocio()
    {
        var timeProvider = new AdjustableFakeTimeProvider(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var storage = new InMemoryWormStorage(timeProvider);
        await storage.WriteAsync(new WormWriteRequest("k1", Content("dato"), TimeSpan.FromDays(30)));

        timeProvider.Advance(TimeSpan.FromDays(29));
        var deleteResult = await storage.DeleteAsync("k1");

        deleteResult.IsFailure.Should().BeTrue();
        deleteResult.Error.Code.Should().Be("Worm.RetentionPeriodNotExpired");
        deleteResult.Error.Type.Should().Be(ErrorType.Conflict);

        // Sigue pudiendo leerse -- la retención bloquea DELETE, no READ.
        var read = await storage.ReadAsync("k1");
        read.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_DespuesDeExpirarLaRetencion_Elimina()
    {
        var timeProvider = new AdjustableFakeTimeProvider(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var storage = new InMemoryWormStorage(timeProvider);
        await storage.WriteAsync(new WormWriteRequest("k1", Content("dato"), TimeSpan.FromDays(30)));

        timeProvider.Advance(TimeSpan.FromDays(30).Add(TimeSpan.FromSeconds(1)));
        var deleteResult = await storage.DeleteAsync("k1");

        deleteResult.IsSuccess.Should().BeTrue();

        var read = await storage.ReadAsync("k1");
        read.IsFailure.Should().BeTrue();
        read.Error.Code.Should().Be("Worm.ObjectNotFound");
    }

    [Fact]
    public async Task WriteAsync_ConUnaClaveYaEliminada_SigueFallando_WriteOnceRealParaSiempre()
    {
        var timeProvider = new AdjustableFakeTimeProvider(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var storage = new InMemoryWormStorage(timeProvider);
        await storage.WriteAsync(new WormWriteRequest("k1", Content("dato"), TimeSpan.FromDays(1)));
        timeProvider.Advance(TimeSpan.FromDays(2));
        (await storage.DeleteAsync("k1")).IsSuccess.Should().BeTrue();

        // Reescribir la misma clave tras eliminarla sigue prohibido -- write-once no es "mientras esté viva".
        var rewrite = await storage.WriteAsync(new WormWriteRequest("k1", Content("fabricado"), TimeSpan.FromDays(1)));

        rewrite.IsFailure.Should().BeTrue();
        rewrite.Error.Code.Should().Be("Worm.ObjectAlreadyExists");
    }

    [Fact]
    public async Task ReadAsync_ConClaveInexistente_Falla()
    {
        var storage = new InMemoryWormStorage();

        var readResult = await storage.ReadAsync("no-existe");

        readResult.IsFailure.Should().BeTrue();
        readResult.Error.Code.Should().Be("Worm.ObjectNotFound");
        readResult.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task DeleteAsync_ConClaveInexistente_Falla()
    {
        var storage = new InMemoryWormStorage();

        var deleteResult = await storage.DeleteAsync("no-existe");

        deleteResult.IsFailure.Should().BeTrue();
        deleteResult.Error.Code.Should().Be("Worm.ObjectNotFound");
    }

    private sealed class AdjustableFakeTimeProvider(DateTime initialUtcNow) : TimeProvider
    {
        private DateTime _utcNow = DateTime.SpecifyKind(initialUtcNow, DateTimeKind.Utc);

        public void Advance(TimeSpan by) => _utcNow = _utcNow.Add(by);

        public override DateTimeOffset GetUtcNow() => new(_utcNow);
    }
}
