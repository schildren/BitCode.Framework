using System.Diagnostics;
using FluentAssertions;

namespace Templates.Tests;

/// <summary>
/// F8-02 (Plan Maestro, Fase 8): verifica que <c>dotnet new bitcode-module</c> genera un módulo
/// (bounded context) REALMENTE compilable -- build de verdad contra los proyectos reales del framework
/// (mismo criterio que <see cref="TemplateVerificationTests"/>) -- Y que sus propias pruebas de límites
/// (<c>ModuleBoundaryTests</c>: el módulo generado no referencia ningún otro módulo de negocio) y de caso
/// de uso (<c>CrearElementoCommandHandlerTests</c>) pasan de verdad con <c>dotnet test</c>. A diferencia de
/// <see cref="AppTemplateVerificationTests"/>, este template genera una biblioteca (no un host ejecutable),
/// así que no hace falta levantar un proceso ni un SQL Server real -- alcanza con build + test.
/// </summary>
public sealed class ModuleTemplateVerificationTests : IDisposable
{
    private readonly string _repoRoot = FindRepoRoot();
    private readonly string _scratchDir = Path.Combine(Path.GetTempPath(), $"bitcode-module-template-{Guid.NewGuid():N}");

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

    [Fact]
    public void ModuleTemplate_GeneratedModule_CompilesAndItsBoundaryAndHandlerTestsPass()
    {
        var (installExitCode, installOutput) = RunDotnet(
            $"new install \"{Path.Combine(_repoRoot, "templates", "module")}\" --force", _repoRoot);
        installExitCode.Should().Be(0, installOutput);

        Directory.CreateDirectory(_scratchDir);
        var sharedSourceRoot = Path.Combine(_repoRoot, "src").Replace('\\', '/');

        // A diferencia de "dotnet new bitcode-app" (AppName solo se usa para el nombre del proyecto, nunca
        // como parte de un identificador C#), acá el nombre del módulo SÍ se usa directamente como prefijo
        // de clase (ModuleNameDbContext, etc.) -- por eso no puede contener un punto (ver README.md del
        // template). "VerificationInventario" en vez de "Verification.Inventario".
        var (generateExitCode, generateOutput) = RunDotnet(
            $"new bitcode-module -n VerificationInventario -o \"{_scratchDir}\" --SharedSourceRoot \"{sharedSourceRoot}\" " +
            "--Namespace Verification.Modules.Inventario",
            _repoRoot);
        generateExitCode.Should().Be(0, generateOutput);

        var moduleCsproj = Directory.GetFiles(_scratchDir, "VerificationInventario.csproj", SearchOption.AllDirectories)
            .Should().ContainSingle("el template debe generar exactamente un .csproj de módulo con el nombre pasado a -n")
            .Subject;

        var (buildExitCode, buildOutput) = RunDotnet("build --nologo -c Debug", Path.GetDirectoryName(moduleCsproj)!);
        buildExitCode.Should().Be(0, buildOutput);

        var testsCsproj = Directory.GetFiles(_scratchDir, "VerificationInventario.Tests.csproj", SearchOption.AllDirectories)
            .Should().ContainSingle("el template debe generar exactamente un proyecto de pruebas del módulo")
            .Subject;

        // dotnet test también hace su propio build -- correrlo alcanza para verificar compilación +
        // ejecución real de ModuleBoundaryTests (límites) y CrearElementoCommandHandlerTests (caso de
        // uso). "console;verbosity=detailed" es necesario para que el logger liste cada prueba individual
        // que PASÓ, no solo las que fallan (la verbosidad por defecto de "dotnet test" omite las
        // exitosas) -- sin esto, esta prueba solo podría verificar el exit code, sin confirmar que las
        // pruebas de límites específicas realmente se descubrieron y corrieron.
        var (testExitCode, testOutput) = RunDotnet(
            "test --nologo -c Debug --logger \"console;verbosity=detailed\"", Path.GetDirectoryName(testsCsproj)!);
        testExitCode.Should().Be(0, testOutput);

        testOutput.Should().Contain("ModuleAssembly_SoloReferenciaSharedYLibreriasDeTercerosPermitidas", testOutput);
        testOutput.Should().Contain("ModuleAssembly_NoReferenciaNingunModuloDePlataformaNiDeOtroBoundedContext", testOutput);
        testOutput.Should().Contain("Handle_ConNombreValido_AgregaElementoAlRepositorioYDevuelveSuId", testOutput);
    }

    public void Dispose()
    {
        if (Directory.Exists(_scratchDir))
        {
            // Best-effort: dotnet test deja algún handle abierto brevemente en Windows en ciertos runs --
            // nunca falla el test por esto (mismo criterio que AppTemplateVerificationTests.DisposeAsync).
            try
            {
                Directory.Delete(_scratchDir, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
