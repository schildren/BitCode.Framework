using FluentAssertions;

namespace BitCode.Architecture.Tests.Compliance;

/// <summary>
/// Pruebas automatizadas de arquitectura y compliance para SBOM y licencias de terceros (Fase 8, F8-14).
/// Valida que el repositorio cumpla con los estándares de trazabilidad, licenciamiento y supply-chain
/// de acuerdo con el Gate de salida de la Fase 8 ("El pipeline produce SBOM, license report y vulnerabilidades").
/// </summary>
public class SbomAndNoticesTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "BitCode.Framework.slnx")))
            {
                return current;
            }
            current = Path.GetDirectoryName(current);
        }
        throw new InvalidOperationException("No se pudo encontrar la raíz del repositorio con BitCode.Framework.slnx.");
    }

    [Fact]
    public void ThirdPartyNotices_ShouldExistAndCoverAllCriticalDependencies()
    {
        var noticesPath = Path.Combine(RepoRoot, "THIRD-PARTY-NOTICES.md");
        File.Exists(noticesPath).Should().BeTrue("El archivo THIRD-PARTY-NOTICES.md debe existir en la raíz.");

        var content = File.ReadAllText(noticesPath);

        // Backend
        content.Should().Contain("MediatR");
        content.Should().Contain("FluentValidation");
        content.Should().Contain("Confluent.Kafka");
        content.Should().Contain("StackExchange.Redis");
        content.Should().Contain("Microsoft.EntityFrameworkCore.SqlServer");

        // Frontend
        content.Should().Contain("@angular/core");
        content.Should().Contain("rxjs");

        // Licencias estándar
        content.Should().Contain("MIT License");
        content.Should().Contain("Apache License 2.0");
    }

    [Fact]
    public void SbomGeneratorScript_ShouldExistInScriptsDirectory()
    {
        var scriptPath = Path.Combine(RepoRoot, "scripts", "generate-sbom-notices.mjs");
        File.Exists(scriptPath).Should().BeTrue("El script scripts/generate-sbom-notices.mjs debe existir.");
    }

    [Fact]
    public void CiWorkflow_ShouldProduceSbomAndLicenseReport()
    {
        var ciPath = Path.Combine(RepoRoot, ".github", "workflows", "ci.yml");
        var content = File.ReadAllText(ciPath);

        content.Should().Contain("generate-sbom-notices.mjs", "ci.yml debe ejecutar el generador de SBOM y notices.");
        content.Should().Contain("sbom-and-license-reports", "ci.yml debe publicar los artefactos de SBOM.");
    }

    [Fact]
    public void ReleaseWorkflow_ShouldAttachSbomToRelease()
    {
        var releasePath = Path.Combine(RepoRoot, ".github", "workflows", "release.yml");
        var content = File.ReadAllText(releasePath);

        content.Should().Contain("generate-sbom-notices.mjs", "release.yml debe generar el SBOM.");
        content.Should().Contain("sbom-artifacts", "release.yml debe empaquetar y subir el SBOM.");
    }
}
