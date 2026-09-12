extern alias SampleApi;
extern alias SampleDocumentsApi;
extern alias SampleWorkflowApi;

using System.Net;
using BitCode.Framework.Shared.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

// Herramienta de desarrollo (no forma parte del build de producción del framework): booteá cada
// Sample.*.Api de referencia contra SQL Server real (Testcontainers, igual que sus propios tests de
// integración -- ver docs/guia-openapi.md) y volcá cada documento OpenAPI generado en tiempo de
// ejecución a un archivo bajo docs/openapi/, para que frontend/packages pueda generar tipos TypeScript
// desde un contrato committeado sin necesitar Docker en cada `npm install`/build.
//
// Uso: dotnet run --project samples/OpenApiExport (requiere Docker corriendo).
//
// Regenerar cuando cambie un contrato HTTP real de alguno de los tres hosts de referencia soportados
// hoy (Sample.Api, Sample.Documents.Api, Sample.Workflow.Api) -- ver docs/guia-contratos-frontend.md.

var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
var outputRoot = Path.Combine(repoRoot, "docs", "openapi");

var exports = new List<(string Name, Func<Task<int>> ExportAsync)>
{
    ("sample-api", () => ExportAsync<SampleApi::Program>(
        "sample-api", "Sample.Api", ["v1", "v2"], requiresIdentityConnectionString: false)),
    ("sample-documents-api", () => ExportAsync<SampleDocumentsApi::Program>(
        "sample-documents-api", "Sample.Documents.Api", ["v1"], requiresIdentityConnectionString: true)),
    ("sample-workflow-api", () => ExportAsync<SampleWorkflowApi::Program>(
        "sample-workflow-api", "Sample.Workflow.Api", ["v1"], requiresIdentityConnectionString: true)),
};

var exitCode = 0;
foreach (var (name, run) in exports)
{
    Console.WriteLine($"==> Exportando OpenAPI de {name}...");
    exitCode |= await run();
}

return exitCode;

async Task<int> ExportAsync<TProgram>(
    string apiName,
    string projectFolderName,
    string[] documentNames,
    bool requiresIdentityConnectionString)
    where TProgram : class
{
    var sqlServerFixture = new SqlServerContainerFixture();
    await sqlServerFixture.InitializeAsync();
    try
    {
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__Default",
            sqlServerFixture.BuildIsolatedConnectionString($"OpenApiExport-{apiName}", "Default"));
        if (requiresIdentityConnectionString)
        {
            Environment.SetEnvironmentVariable(
                "ConnectionStrings__Identity",
                sqlServerFixture.BuildIsolatedConnectionString($"OpenApiExport-{apiName}", "Identity"));
        }

        // WebApplicationFactory<TProgram> infiere el content root asumiendo que TProgram vive
        // directamente bajo la raíz del repo (heurística basada en el nombre del ensamblado sobre el
        // .slnx) -- en este repo cada Sample.*.Api vive bajo samples/, así que la heurística
        // automática falla (DirectoryNotFoundException) aun cuando MvcTestingAppManifest.json (generado
        // por el paquete Microsoft.AspNetCore.Mvc.Testing) sí tiene la ruta correcta. UseContentRoot
        // fuerza explícitamente la ruta real del proyecto de referencia.
        var contentRoot = Path.Combine(repoRoot, "samples", projectFolderName);

        await using var factory = new WebApplicationFactory<TProgram>()
            .WithWebHostBuilder(builder => builder.UseContentRoot(contentRoot));
        using var client = factory.CreateClient();

        var outputDir = Path.Combine(outputRoot, apiName);
        Directory.CreateDirectory(outputDir);

        foreach (var documentName in documentNames)
        {
            var response = await client.GetAsync($"/openapi/{documentName}.json");
            if (response.StatusCode != HttpStatusCode.OK)
            {
                Console.Error.WriteLine(
                    $"    ERROR: /openapi/{documentName}.json de {apiName} respondió {(int)response.StatusCode}.");
                return 1;
            }

            var json = await response.Content.ReadAsStringAsync();
            var outputFile = Path.Combine(outputDir, $"{documentName}.json");
            await File.WriteAllTextAsync(outputFile, json);
            Console.WriteLine($"    {Path.GetRelativePath(outputRoot, outputFile).Replace('\\', '/')} OK");
        }

        return 0;
    }
    finally
    {
        await sqlServerFixture.DisposeAsync();
    }
}

static string FindRepoRoot(string startDirectory)
{
    var current = new DirectoryInfo(startDirectory);
    while (current is not null && !File.Exists(Path.Combine(current.FullName, "BitCode.Framework.slnx")))
    {
        current = current.Parent;
    }

    return current?.FullName
        ?? throw new InvalidOperationException("No se encontró BitCode.Framework.slnx subiendo desde " + startDirectory);
}
