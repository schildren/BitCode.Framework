using FluentAssertions;

namespace BitCode.Architecture.Tests.Release;

/// <summary>
/// Pruebas automatizadas de arquitectura y consistencia de manifiestos de release (Fase 8, F8-13).
/// Valida que el pipeline de release, scripts y changelog cumplan con los estándares requeridos
/// para el Gate de salida ("Los paquetes tienen versionado, firma y provenance").
/// </summary>
public class ReleaseManifestTests
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
    public void ReleaseWorkflow_ShouldExistAndDeclareRequiredTriggers()
    {
        var workflowPath = Path.Combine(RepoRoot, ".github", "workflows", "release.yml");
        File.Exists(workflowPath).Should().BeTrue("El workflow de release .github/workflows/release.yml debe existir.");

        var content = File.ReadAllText(workflowPath);

        content.Should().Contain("tags:");
        content.Should().Contain("\"v*\"");
        content.Should().Contain("workflow_dispatch:");
    }

    [Fact]
    public void ReleaseWorkflow_ShouldDeclarePackagingSigningProvenanceAndPublishingSteps()
    {
        var workflowPath = Path.Combine(RepoRoot, ".github", "workflows", "release.yml");
        var content = File.ReadAllText(workflowPath);

        content.Should().Contain("dotnet pack", "Debe empaquetar los proyectos .NET con Source Link y MinVer.");
        content.Should().Contain("actions/attest-build-provenance", "Debe generar atestaciones de procedencia SLSA para los paquetes.");
        content.Should().Contain("dotnet nuget push", "Debe publicar los paquetes al feed de NuGet.");
        content.Should().Contain("npx nx release", "Debe versionar y publicar los paquetes npm de frontend.");
        content.Should().Contain("gh release create", "Debe crear el release oficial en GitHub con las notas y binarios.");
    }

    [Fact]
    public void ReleaseScripts_ShouldExistInScriptsDirectory()
    {
        var changelogScript = Path.Combine(RepoRoot, "scripts", "generate-changelog.mjs");
        var releasePs1 = Path.Combine(RepoRoot, "scripts", "release.ps1");
        var releaseSh = Path.Combine(RepoRoot, "scripts", "release.sh");

        File.Exists(changelogScript).Should().BeTrue("scripts/generate-changelog.mjs debe existir.");
        File.Exists(releasePs1).Should().BeTrue("scripts/release.ps1 debe existir.");
        File.Exists(releaseSh).Should().BeTrue("scripts/release.sh debe existir.");
    }

    [Fact]
    public void Changelog_ShouldExistAndContainConventionalCommitsStructure()
    {
        var changelogPath = Path.Combine(RepoRoot, "CHANGELOG.md");
        File.Exists(changelogPath).Should().BeTrue("El archivo CHANGELOG.md debe existir en la raíz del repositorio.");

        var content = File.ReadAllText(changelogPath);
        content.Should().Contain("# Changelog — BitCode.Framework");
        content.Should().Contain("Conventional Commits");
    }
}
