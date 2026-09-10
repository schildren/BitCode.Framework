using System.Diagnostics;
using FluentAssertions;

namespace Templates.Tests;

/// <summary>
/// Verifica que las plantillas dotnet new (Fase 6) no solo se "instalan sin error", sino que el
/// código que generan compila de verdad referenciando los proyectos reales del framework — la
/// misma vara de medir que el resto de la Fase 6 en adelante: ejecutar el camino real, no confiar
/// en que la plantilla "se ve bien".
/// </summary>
/// <remarks>
/// El template <c>bitcode-feature</c> original de esta fase (<c>templates/feature-cqrs</c>, tres archivos
/// sueltos por Command, sin Query ni Endpoint) fue reemplazado por <c>templates/feature</c> (F8-03, Plan
/// Maestro Fase 8): mismo shortName <c>bitcode-feature</c>, pero vertical slice completo (Command o Query
/// + Validator + Handler + Endpoint) pensado para insertarse dentro de un módulo ya generado por
/// <c>dotnet new bitcode-module</c> (no contra un proyecto de verificación aislado, porque el Endpoint
/// generado necesita <c>Shared.Infrastructure.Web</c> y el <c>FrameworkReference</c> a
/// <c>Microsoft.AspNetCore.App</c> que ya trae cualquier módulo real). Ver
/// <see cref="FeatureTemplateVerificationTests"/> para su verificación end-to-end.
/// </remarks>
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
                 <TargetFramework>net10.0</TargetFramework>
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
