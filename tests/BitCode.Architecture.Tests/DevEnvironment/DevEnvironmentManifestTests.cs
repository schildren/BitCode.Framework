using System.IO;
using FluentAssertions;
using Xunit;

namespace BitCode.Framework.Architecture.Tests.DevEnvironment;

/// <summary>
/// F8-11: Pruebas de verificación sobre los manifiestos y configuraciones del entorno local
/// de desarrollo (docker-compose.yml, scripts y otel-collector-local.yaml).
/// Garantiza que los archivos requeridos para el onboarding existen, están bien formados
/// y exponen los puertos e imágenes estandarizadas.
/// </summary>
public class DevEnvironmentManifestTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BitCode.Framework.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("No se encontró la raíz del repo (BitCode.Framework.slnx).");
    }

    [Fact]
    public void DockerCompose_Manifest_Exists_And_Contains_Essential_Services()
    {
        var composePath = Path.Combine(RepoRoot, "docker-compose.yml");
        File.Exists(composePath).Should().BeTrue("docker-compose.yml debe existir en la raíz del repositorio.");

        var content = File.ReadAllText(composePath);

        // Servicios requeridos
        content.Should().Contain("sqlserver:", "Debe definir el servicio SQL Server.");
        content.Should().Contain("redis:", "Debe definir el servicio Redis.");
        content.Should().Contain("kafka:", "Debe definir el servicio Kafka.");
        content.Should().Contain("otel-collector:", "Debe definir el servicio OpenTelemetry Collector.");
        content.Should().Contain("jaeger:", "Debe definir el servicio Jaeger.");

        // Puertos estándar expuestos
        content.Should().Contain("\"1433:1433\"", "SQL Server debe exponer el puerto 1433.");
        content.Should().Contain("\"6379:6379\"", "Redis debe exponer el puerto 6379.");
        content.Should().Contain("\"9092:9092\"", "Kafka debe exponer el puerto 9092.");
        content.Should().Contain("\"4317:4317\"", "OTel Collector debe exponer gRPC en el puerto 4317.");
        content.Should().Contain("\"16686:16686\"", "Jaeger debe exponer su UI en el puerto 16686.");
    }

    [Fact]
    public void OTelCollector_Local_Config_Exists_And_Defines_Pipelines()
    {
        var configPath = Path.Combine(RepoRoot, "docker", "otel-collector-local.yaml");
        File.Exists(configPath).Should().BeTrue("docker/otel-collector-local.yaml debe existir.");

        var content = File.ReadAllText(configPath);
        content.Should().Contain("health_check:", "Debe habilitar la extensión health_check.");
        content.Should().Contain("otlp/jaeger:", "Debe definir el exportador hacia Jaeger.");
        content.Should().Contain("traces:", "Debe definir el pipeline de trazas.");
    }

    [Fact]
    public void DevEnv_Management_Scripts_Exist()
    {
        var ps1Path = Path.Combine(RepoRoot, "scripts", "dev-env.ps1");
        var shPath = Path.Combine(RepoRoot, "scripts", "dev-env.sh");

        File.Exists(ps1Path).Should().BeTrue("scripts/dev-env.ps1 debe existir.");
        File.Exists(shPath).Should().BeTrue("scripts/dev-env.sh debe existir.");
    }
}
