using System.Diagnostics;
using FluentAssertions;

namespace Templates.Tests;

/// <summary>
/// Verifica que las plantillas dotnet new (Fase 6) no solo se "instalan sin error", sino que el
/// código que generan compila de verdad referenciando los proyectos reales del framework — la
/// misma vara de medir que el resto de la Fase 6 en adelante: ejecutar el camino real, no confiar
/// en que la plantilla "se ve bien".
/// </summary>
public class TemplateVerificationTests : IDisposable
{
    private readonly string _repoRoot = FindRepoRoot();
    private readonly string _scratchDir = Path.Combine(Path.GetTempPath(), $"bitcode-templates-{Guid.NewGuid():N}");

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

    private static (int ExitCode, string Output) RunDotnet(string arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo("dotnet", arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, output);
    }

    private void InstallTemplates()
    {
        RunDotnet($"new install \"{Path.Combine(_repoRoot, "templates", "feature-cqrs")}\" --force", _repoRoot);
        RunDotnet($"new install \"{Path.Combine(_repoRoot, "templates", "domain-entity")}\" --force", _repoRoot);
    }

    private string CreateVerificationProject(string projectName, params string[] projectReferences)
    {
        var projectDir = Path.Combine(_scratchDir, projectName);
        Directory.CreateDirectory(projectDir);

        var references = string.Join(
            Environment.NewLine,
            projectReferences.Select(reference => $"    <ProjectReference Include=\"{reference}\" />"));

        File.WriteAllText(
            Path.Combine(projectDir, $"{projectName}.csproj"),
            $"""
             <Project Sdk="Microsoft.NET.Sdk">
               <PropertyGroup>
                 <TargetFramework>net8.0</TargetFramework>
                 <ImplicitUsings>enable</ImplicitUsings>
                 <Nullable>enable</Nullable>
               </PropertyGroup>
               <ItemGroup>
             {references}
               </ItemGroup>
             </Project>
             """);

        return projectDir;
    }

    [Fact]
    public void FeatureCqrsTemplate_GeneratedCode_CompilesAgainstRealFrameworkProjects()
    {
        InstallTemplates();
        var projectDir = CreateVerificationProject(
            "FeatureVerification",
            Path.Combine(_repoRoot, "src", "Shared.Application", "Shared.Application.csproj"));

        var (generateExitCode, generateOutput) = RunDotnet(
            "new bitcode-feature -n CrearProducto --Namespace Verification.Features",
            projectDir);
        generateExitCode.Should().Be(0, generateOutput);

        var (buildExitCode, buildOutput) = RunDotnet("build --nologo", projectDir);
        buildExitCode.Should().Be(0, buildOutput);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public void DomainEntityTemplate_GeneratedCode_CompilesWithAndWithoutMultiTenant(string multiTenant)
    {
        InstallTemplates();
        var projectDir = CreateVerificationProject(
            $"EntityVerification{multiTenant}",
            Path.Combine(_repoRoot, "src", "Shared.Kernel", "Shared.Kernel.csproj"),
            Path.Combine(_repoRoot, "src", "Shared.Domain", "Shared.Domain.csproj"));

        var (generateExitCode, generateOutput) = RunDotnet(
            $"new bitcode-entity -n Producto --Namespace Verification.Entities --MultiTenant {multiTenant}",
            projectDir);
        generateExitCode.Should().Be(0, generateOutput);

        var generatedFile = File.ReadAllText(Path.Combine(projectDir, "Producto.cs"));
        if (multiTenant == "true")
        {
            generatedFile.Should().Contain("ITenantEntity").And.Contain("TenantId");
        }
        else
        {
            generatedFile.Should().NotContain("ITenantEntity").And.NotContain("TenantId");
        }

        var (buildExitCode, buildOutput) = RunDotnet("build --nologo", projectDir);
        buildExitCode.Should().Be(0, buildOutput);
    }

    public void Dispose()
    {
        if (Directory.Exists(_scratchDir))
        {
            Directory.Delete(_scratchDir, recursive: true);
        }
    }
}
