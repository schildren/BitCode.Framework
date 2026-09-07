using BitCode.Framework.Shared.Infrastructure.Security.Audit;
using BitCode.Framework.Shared.Infrastructure.Security.FeatureFlags;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.FeatureFlags;

/// <summary>
/// Cierre del gap documentado en <c>docs/politica-configuracion-y-feature-flags.md</c> sección 2.4
/// ("Límite real del hot-reload con el ConfigMap actual"): a diferencia de
/// <see cref="FeatureFlagChangeAuditingServiceTests"/> (que simula la recarga con un
/// <c>IConfigurationProvider</c> de prueba que dispara <c>OnReload</c> a mano), esta prueba usa el
/// mecanismo REAL de producción -- <c>Microsoft.Extensions.Configuration.Json</c>
/// (<c>AddJsonFile(path, optional: true, reloadOnChange: true)</c>) leyendo un archivo físico en disco y
/// modificándolo mientras el host corre.
/// <para>
/// Esto es exactamente lo que ocurre en Kubernetes cuando un <c>ConfigMap</c> se monta como VOLUMEN
/// (<c>k8s/sample-api/base/featureflags-configmap.yaml</c> + el <c>volumeMounts</c> de
/// <c>k8s/sample-api/base/deployment.yaml</c>, sin <c>subPath</c>): el kubelet resincroniza el archivo
/// del volumen cuando el <c>ConfigMap</c> cambia (con el delay propio del "sync period" del kubelet), y
/// el <c>FileSystemWatcher</c> subyacente de <c>PhysicalFileProvider</c> dispara el change token que
/// <c>reloadOnChange: true</c> necesita -- el proceso .NET nunca sabe que está corriendo dentro de un
/// pod, solo ve un archivo que cambió en disco. Este test reemplaza al kubelet escribiendo el archivo
/// directamente.
/// </para>
/// </summary>
public class FeatureFlagsFileHotReloadIntegrationTests : IAsyncLifetime
{
    private const string FlagName = "NuevoFlujoDePagos";

    private string _filePath = null!;
    private IHost _host = null!;
    private CapturingAuditWriter _auditWriter = null!;

    public async Task InitializeAsync()
    {
        _filePath = Path.Combine(Path.GetTempPath(), $"featureflags-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(_filePath, WriteFlagsJson(enabled: false));

        var builder = Host.CreateApplicationBuilder();

        // Mismo método que Sample.Api/Program.cs usa contra el path montado por
        // k8s/sample-api/base/deployment.yaml ("/app/config/featureflags.json") -- acá apunta al archivo
        // temporal del test, pero el mecanismo (AddJsonFile con reloadOnChange) es idéntico.
        builder.Configuration.AddJsonFile(_filePath, optional: true, reloadOnChange: true);

        builder.Services.AddSharedFeatureFlags(builder.Configuration);

        // Reemplaza IAuditWriter real (que exigiría persistencia/hash-chain, F2-15 a F2-20) por un writer
        // de prueba que permite esperar de forma determinística la escritura de auditoría -- mismo patrón
        // que FeatureFlagChangeAuditingServiceTests, reutilizando CapturingAuditWriter.
        _auditWriter = new CapturingAuditWriter();
        builder.Services.RemoveAll<IAuditWriter>();
        builder.Services.AddSingleton<IAuditWriter>(_auditWriter);

        _host = builder.Build();
        await _host.StartAsync();
    }

    [Fact]
    public async Task ArchivoDeConfigModificadoEnDisco_RecargaElFlagYAuditaLaTransicionSinReiniciarElProceso()
    {
        var provider = _host.Services.GetRequiredService<IFeatureFlagProvider>();
        provider.IsEnabled(FlagName).Should().BeFalse("el archivo inicial declara el flag en false");

        // Simula lo que el kubelet hace al resincronizar un volumen de ConfigMap: reescribe el archivo
        // montado EN EL MISMO PATH, sin reiniciar el proceso que lo lee.
        await File.WriteAllTextAsync(_filePath, WriteFlagsJson(enabled: true));

        var written = await _auditWriter.WaitForNextAsync();

        written.Action.Should().Be(FeatureFlagChangeAuditingService.AuditAction);
        written.Resource.Type.Should().Be(FeatureFlagChangeAuditingService.AuditResourceType);
        written.Resource.Id.Should().Be(FlagName);
        written.Actor.Id.Should().Be(FeatureFlagChangeAuditingService.SystemActorId);
        written.Actor.Type.Should().Be(AuditActorType.System);
        written.Metadata["oldValue"].Should().Be("False");
        written.Metadata["newValue"].Should().Be("True");

        // El mismo IFeatureFlagProvider obtenido ANTES de la recarga ya refleja el valor nuevo -- prueba
        // que IOptionsMonitor<FeatureFlagsOptions>.CurrentValue (ConfigurationFeatureFlagProvider.IsEnabled)
        // no requiere resolver una instancia nueva ni reiniciar el proceso.
        provider.IsEnabled(FlagName).Should().BeTrue("el archivo se reescribió con el flag en true, sin reiniciar el host");
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();

        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }
    }

    private static string WriteFlagsJson(bool enabled) =>
        $$"""
        {
          "FeatureFlags": {
            "{{FlagName}}": {{(enabled ? "true" : "false")}}
          }
        }
        """;
}
