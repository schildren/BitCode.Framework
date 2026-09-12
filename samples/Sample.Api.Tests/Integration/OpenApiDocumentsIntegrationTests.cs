using System.Net;
using BitCode.Framework.Shared.Testing;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;

namespace Sample.Api.Tests.Integration;

/// <summary>
/// F1-28: verifica de punta a punta, contra el servidor real de Sample.Api (Testcontainers, SQL
/// Server), el criterio de aceptación literal del backlog ("Validación automática"): el documento
/// OpenAPI que genera <c>Microsoft.AspNetCore.OpenApi</c> en tiempo de ejecución (nunca un archivo
/// estático mantenido a mano) es un documento OpenAPI VÁLIDO, verificado parseándolo con el propio
/// modelo de objetos de <c>Microsoft.OpenApi</c> (<see cref="OpenApiDocument.Parse(string, string,
/// OpenApiReaderSettings?)"/>, que aplica las mismas reglas de esquema que cualquier lector de OpenAPI
/// de terceros) y confirmando que <see cref="OpenApiDiagnostic.Errors"/> queda vacío -- no alcanza con
/// "se generó sin excepción" ni con una inspección visual: un JSON sintácticamente válido pero
/// semánticamente incompleto (por ejemplo, una respuesta sin <c>description</c>, requerida por el
/// esquema de OpenAPI) reporta errores acá igual que lo haría cualquier otro validador de contrato.
/// </summary>
[Collection(SampleApiSequentialCollection.Name)]
public class OpenApiDocumentsIntegrationTests : IAsyncLifetime
{
    private readonly SqlServerContainerFixture _sqlServerFixture = new();
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        await _sqlServerFixture.InitializeAsync();

        Environment.SetEnvironmentVariable(
            "ConnectionStrings__Default",
            _sqlServerFixture.BuildIsolatedConnectionString("SampleApiOpenApi", nameof(OpenApiDocumentsIntegrationTests)));

        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();
    }

    private async Task<OpenApiDocument> ObtenerDocumentoValidadoAsync(string documentName)
    {
        var response = await _client!.GetAsync($"/openapi/{documentName}.json");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();

        // Validación automática real (no "se generó sin tirar excepción"): OpenApiDocument.Parse
        // aplica el esquema de OpenAPI -- ver el resumen de la clase.
        var result = OpenApiDocument.Parse(json, "json");

        result.Diagnostic!.Errors.Should().BeEmpty(
            $"el documento '{documentName}' generado por Microsoft.AspNetCore.OpenApi debe ser un OpenAPI válido");
        result.Document.Should().NotBeNull();

        return result.Document!;
    }

    [Fact]
    public async Task DocumentoV1_EsUnOpenApiValido()
    {
        await ObtenerDocumentoValidadoAsync("v1");
    }

    [Fact]
    public async Task DocumentoV2_EsUnOpenApiValido()
    {
        await ObtenerDocumentoValidadoAsync("v2");
    }

    /// <summary>
    /// F1-28 (alcance "generación" + "un documento separado por versión", F1-27): v1 documenta el
    /// contrato de creación/listado/obtención v1 -- pero NO el endpoint que solo existe en v2 -- para
    /// que los dos documentos nunca mezclen los contratos de ambas versiones bajo el mismo path.
    /// </summary>
    [Fact]
    public async Task DocumentoV1_DocumentaEndpointsV1_YExcluyeElContratoExclusivoDeV2()
    {
        var document = await ObtenerDocumentoValidadoAsync("v1");

        var pathProductos = document.Paths.Keys.Should()
            .ContainSingle(p => p.EndsWith("/productos", StringComparison.Ordinal))
            .Subject;
        var pathProductoPorId = document.Paths.Keys.Should()
            .ContainSingle(p => p.EndsWith("/productos/{id}", StringComparison.Ordinal))
            .Subject;

        document.Paths[pathProductos].Operations.Should().ContainKey(HttpMethod.Post);
        document.Paths[pathProductos].Operations.Should().ContainKey(HttpMethod.Get);
        document.Paths[pathProductoPorId].Operations.Should().ContainKey(HttpMethod.Get);

        // El contrato v2 (ProductoResponseV2, con CreadoEnUtc) NO debe filtrarse al documento v1: el
        // 200 de v1 documenta el schema de ProductoResponse (Id/Nombre/Precio), sin CreadoEnUtc.
        var respuestaOk = document.Paths[pathProductoPorId].Operations![HttpMethod.Get]!.Responses!["200"];
        var schema = respuestaOk.Content!["application/json"].Schema;
        schema!.Properties.Should().NotContainKey("creadoEnUtc");
    }

    /// <summary>
    /// F1-28: v2 documenta el contrato v2 de "obtener producto" (con <c>CreadoEnUtc</c>, que v1 nunca
    /// expuso) bajo el MISMO path relativo que v1 -- demuestra que el filtrado por documento evita que
    /// ambos contratos se pisen en el mismo <c>PathItem</c>.
    /// </summary>
    [Fact]
    public async Task DocumentoV2_DocumentaElContratoV2_ConCreadoEnUtc()
    {
        var document = await ObtenerDocumentoValidadoAsync("v2");

        var pathProductoPorId = document.Paths.Keys.Should()
            .ContainSingle(p => p.EndsWith("/productos/{id}", StringComparison.Ordinal))
            .Subject;

        document.Paths[pathProductoPorId].Operations.Should().ContainKey(HttpMethod.Get);
        var respuestaOk = document.Paths[pathProductoPorId].Operations![HttpMethod.Get]!.Responses!["200"];
        var schema = respuestaOk.Content!["application/json"].Schema;

        schema!.Properties.Should().ContainKey("creadoEnUtc");

        // F1-28 (examples): GET v2 tiene un ejemplo real de response (ProductosExampleOperationTransformer),
        // no solo el schema inferido por reflexión.
        respuestaOk.Content!["application/json"].Example.Should().NotBeNull();
    }

    /// <summary>
    /// F1-28 (errores estandarizados): crear un producto documenta sus respuestas de error reales
    /// (400 validación/idempotencia, 409 idempotencia reutilizada con otro body) -- nunca solo el 201
    /// feliz.
    /// </summary>
    [Fact]
    public async Task DocumentoV1_CrearProducto_DocumentaRespuestasDeErrorEstandarizadas()
    {
        var document = await ObtenerDocumentoValidadoAsync("v1");

        var pathProductos = document.Paths.Keys.Should()
            .ContainSingle(p => p.EndsWith("/productos", StringComparison.Ordinal))
            .Subject;

        var operacionCrear = document.Paths[pathProductos].Operations![HttpMethod.Post]!;

        operacionCrear.Responses.Should().ContainKey("201");
        operacionCrear.Responses.Should().ContainKey("400");
        operacionCrear.Responses.Should().ContainKey("409");

        // F1-28 (examples): el request body de creación tiene un ejemplo real.
        operacionCrear.RequestBody!.Content!["application/json"].Example.Should().NotBeNull();
    }

    /// <summary>
    /// F1-28: <c>/health/live</c>/<c>/health/ready</c> (<c>MapHealthChecks</c>, F1-25) NO aparecen en
    /// ningún documento OpenAPI -- verificado acá para dejar constancia explícita de un límite real
    /// del generador nativo, no una omisión de <see cref="OpenApiServiceCollectionExtensions.AddSharedOpenApiForApiVersion"/>:
    /// <c>MapHealthChecks</c> mapea un <c>RequestDelegate</c> plano (sin pasar por
    /// <c>RequestDelegateFactory</c>, el mecanismo que sí anota metadata de ApiExplorer a los
    /// delegados de <c>MapGet</c>/<c>MapPost</c>/etc.), así que
    /// <c>IApiDescriptionGroupCollectionProvider</c> nunca genera una <c>ApiDescription</c> para esos
    /// dos endpoints -- <see cref="OpenApiOptions.ShouldInclude"/> ni siquiera llega a evaluarlos.
    /// Documentado en <c>docs/guia-openapi.md</c> en vez de forzarlos con metadata manual: son
    /// endpoints operativos para un orquestador/balanceador, no parte del contrato de negocio
    /// versionado que consume un cliente de la API.
    /// </summary>
    [Fact]
    public async Task NingunDocumento_IncluyeLosEndpointsDeHealthChecks()
    {
        var documentoV1 = await ObtenerDocumentoValidadoAsync("v1");
        var documentoV2 = await ObtenerDocumentoValidadoAsync("v2");

        documentoV1.Paths.Should().NotContainKey("/health/live");
        documentoV1.Paths.Should().NotContainKey("/health/ready");
        documentoV2.Paths.Should().NotContainKey("/health/live");
        documentoV2.Paths.Should().NotContainKey("/health/ready");
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _sqlServerFixture.DisposeAsync();
    }
}
