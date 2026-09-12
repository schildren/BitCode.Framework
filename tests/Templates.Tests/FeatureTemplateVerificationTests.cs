using System.Diagnostics;
using FluentAssertions;

namespace Templates.Tests;

/// <summary>
/// F8-03 (Plan Maestro, Fase 8): verifica que <c>dotnet new bitcode-feature</c> genera un vertical slice
/// CQRS REALMENTE insertable dentro de un módulo ya existente -- a diferencia de
/// <see cref="ModuleTemplateVerificationTests"/> (que genera un proyecto nuevo desde cero),
/// <c>bitcode-feature</c> es un template de tipo "item": no crea ningún <c>.csproj</c>, solo agrega
/// archivos sueltos al directorio donde se lo corre. Esta prueba reproduce el flujo real end-to-end:
/// genera un módulo con <c>dotnet new bitcode-module</c> (F8-02), corre
/// <c>dotnet new bitcode-feature</c> DENTRO de su carpeta <c>Elementos/</c> una vez en modo Command y otra
/// en modo Query, wirea ambos endpoints generados al <c>*EndpointRouteBuilderExtensions.cs</c> del módulo
/// (el mismo paso manual que el README del template le pide al desarrollador) y confirma que el módulo
/// resultante compila y que sus propias pruebas (<c>ModuleBoundaryTests</c>,
/// <c>CrearElementoCommandHandlerTests</c>) siguen pasando con el vertical slice nuevo adentro.
/// </summary>
public sealed class FeatureTemplateVerificationTests : IDisposable
{
    private readonly string _repoRoot = FindRepoRoot();
    private readonly string _scratchDir = Path.Combine(Path.GetTempPath(), $"bitcode-feature-template-{Guid.NewGuid():N}");

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
    public void FeatureTemplate_InsertedIntoGeneratedModule_CompilesAndModuleTestsStillPass()
    {
        var (installModuleExitCode, installModuleOutput) = RunDotnet(
            $"new install \"{Path.Combine(_repoRoot, "templates", "module")}\" --force", _repoRoot);
        installModuleExitCode.Should().Be(0, installModuleOutput);

        var (installFeatureExitCode, installFeatureOutput) = RunDotnet(
            $"new install \"{Path.Combine(_repoRoot, "templates", "feature")}\" --force", _repoRoot);
        installFeatureExitCode.Should().Be(0, installFeatureOutput);

        Directory.CreateDirectory(_scratchDir);
        var sharedSourceRoot = Path.Combine(_repoRoot, "src").Replace('\\', '/');

        var (generateModuleExitCode, generateModuleOutput) = RunDotnet(
            $"new bitcode-module -n VerificationCatalogo -o \"{_scratchDir}\" --SharedSourceRoot \"{sharedSourceRoot}\" " +
            "--Namespace Verification.Modules.Catalogo",
            _repoRoot);
        generateModuleExitCode.Should().Be(0, generateModuleOutput);

        var elementosDir = Directory.GetDirectories(_scratchDir, "Elementos", SearchOption.AllDirectories)
            .Should().ContainSingle("el módulo generado debe tener exactamente una carpeta Elementos/ (el agregado de ejemplo)")
            .Subject;

        // bitcode-feature es un template de tipo "item": se corre DENTRO del directorio destino (acá,
        // Elementos/ del módulo recién generado) y agrega archivos sueltos -- no crea ningún .csproj propio.
        var (generateCommandExitCode, generateCommandOutput) = RunDotnet(
            "new bitcode-feature -n ActualizarElemento --Namespace Verification.Modules.Catalogo.Elementos " +
            "--Kind Command --ResponseType Guid",
            elementosDir);
        generateCommandExitCode.Should().Be(0, generateCommandOutput);

        var (generateQueryExitCode, generateQueryOutput) = RunDotnet(
            "new bitcode-feature -n BuscarElemento --Namespace Verification.Modules.Catalogo.Elementos --Kind Query",
            elementosDir);
        generateQueryExitCode.Should().Be(0, generateQueryOutput);

        File.Exists(Path.Combine(elementosDir, "ActualizarElementoCommand.cs")).Should().BeTrue();
        File.Exists(Path.Combine(elementosDir, "ActualizarElementoCommandEndpoints.cs")).Should().BeTrue();
        File.Exists(Path.Combine(elementosDir, "BuscarElementoQuery.cs")).Should().BeTrue();
        File.Exists(Path.Combine(elementosDir, "BuscarElementoQueryEndpoints.cs")).Should().BeTrue();

        // El template de Query no debe haber pisado los archivos de Command generados en el paso anterior
        // (ni viceversa) -- cada modificador de exclusión de template.json actúa solo sobre el Kind pedido.
        File.Exists(Path.Combine(elementosDir, "BuscarElementoCommand.cs")).Should().BeFalse();
        File.Exists(Path.Combine(elementosDir, "ActualizarElementoQuery.cs")).Should().BeFalse();

        WireEndpointsIntoModule(_scratchDir);

        var moduleCsproj = Directory.GetFiles(_scratchDir, "VerificationCatalogo.csproj", SearchOption.AllDirectories)
            .Should().ContainSingle("el template de módulo debe generar exactamente un .csproj con el nombre pasado a -n")
            .Subject;

        var (buildExitCode, buildOutput) = RunDotnet("build --nologo -c Debug", Path.GetDirectoryName(moduleCsproj)!);
        buildExitCode.Should().Be(0, buildOutput);

        var testsCsproj = Directory.GetFiles(_scratchDir, "VerificationCatalogo.Tests.csproj", SearchOption.AllDirectories)
            .Should().ContainSingle("el template de módulo debe generar exactamente un proyecto de pruebas")
            .Subject;

        // dotnet test también hace su propio build -- correrlo alcanza para verificar que el vertical
        // slice nuevo compila integrado al módulo Y que las pruebas propias del módulo (límites + caso de
        // uso de ejemplo) siguen pasando con el feature nuevo adentro.
        var (testExitCode, testOutput) = RunDotnet(
            "test --nologo -c Debug --logger \"console;verbosity=detailed\"", Path.GetDirectoryName(testsCsproj)!);
        testExitCode.Should().Be(0, testOutput);

        testOutput.Should().Contain("ModuleAssembly_SoloReferenciaSharedYLibreriasDeTercerosPermitidas", testOutput);
        testOutput.Should().Contain("ModuleAssembly_NoReferenciaNingunModuloDePlataformaNiDeOtroBoundedContext", testOutput);
        testOutput.Should().Contain("Handle_ConNombreValido_AgregaElementoAlRepositorioYDevuelveSuId", testOutput);
    }

    /// <summary>
    /// Reproduce el paso manual que <c>templates/feature/README.md</c> le pide al desarrollador: wirear
    /// el <c>Map*Endpoint</c> de cada feature generado sobre el <c>MapGroup</c> ya versionado y autorizado
    /// del módulo, en su <c>*EndpointRouteBuilderExtensions.cs</c> agregador.
    /// </summary>
    private static void WireEndpointsIntoModule(string scratchDir)
    {
        var endpointsFile = Directory.GetFiles(
                scratchDir, "VerificationCatalogoEndpointRouteBuilderExtensions.cs", SearchOption.AllDirectories)
            .Should().ContainSingle("el template de módulo debe generar exactamente un archivo de endpoints agregador")
            .Subject;

        var content = File.ReadAllText(endpointsFile);
        content.Should().Contain("return endpoints;");

        var wiring =
            """
                    elementos.MapActualizarElementoEndpoint()
                        .RequireAuthorization(VerificationCatalogoPermissions.ElementosAdministrar);

                    elementos.MapBuscarElementoEndpoint()
                        .RequireAuthorization(VerificationCatalogoPermissions.ElementosVer);

            """;

        content = content.Replace("        return endpoints;", wiring + "        return endpoints;");
        File.WriteAllText(endpointsFile, content);
    }

    public void Dispose()
    {
        if (Directory.Exists(_scratchDir))
        {
            // Best-effort: dotnet test deja algún handle abierto brevemente en Windows en ciertos runs --
            // nunca falla el test por esto (mismo criterio que ModuleTemplateVerificationTests.Dispose).
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
