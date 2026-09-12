using System.Net;
using System.Net.Http.Json;
using BitCode.Framework.Shared.Testing;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Sample.Api.Tests.Integration;

/// <summary>
/// F1-27: verifica de punta a punta, contra el servidor real de Sample.Api (Testcontainers, SQL
/// Server), el criterio de aceptación literal del backlog ("versiones coexistentes"): v1 y v2 de
/// <c>GET /api/v{version}/productos/{id}</c> conviven en el mismo despliegue, sin que agregar v2
/// rompa ni reemplace a v1. También verifica los dos comportamientos restantes exigidos por la
/// tarea: una versión no declarada responde con un error claro (no un 200/500 silencioso), y la
/// deprecación de una versión agrega el header correspondiente.
/// </summary>
[Collection(SampleApiSequentialCollection.Name)]
public class ApiVersioningIntegrationTests : IAsyncLifetime
{
    private readonly SqlServerContainerFixture _sqlServerFixture = new();
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        await _sqlServerFixture.InitializeAsync();

        Environment.SetEnvironmentVariable(
            "ConnectionStrings__Default",
            _sqlServerFixture.BuildIsolatedConnectionString("SampleApiTests", nameof(ApiVersioningIntegrationTests)));

        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();
    }

    private async Task<Guid> CrearProductoAsync(string nombre, decimal precio)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/productos")
        {
            Content = JsonContent.Create(new { nombre, precio }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await _client!.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return await response.Content.ReadFromJsonAsync<Guid>();
    }

    /// <summary>
    /// Criterio de aceptación literal de F1-27 ("versiones coexistentes"): el MISMO recurso
    /// (mismo producto, mismo path relativo <c>/productos/{id}</c>) responde con dos contratos
    /// distintos según el segmento de versión de la ruta, en el mismo proceso/despliegue, sin que
    /// pedir v2 afecte a v1 ni viceversa.
    /// </summary>
    [Fact]
    public async Task ObtenerProducto_V1YV2_CoexistenSinRomperseMutuamente()
    {
        var id = await CrearProductoAsync("Teclado versionado", 49.90m);

        var respuestaV1 = await _client!.GetAsync($"/api/v1/productos/{id}");
        var respuestaV2 = await _client!.GetAsync($"/api/v2/productos/{id}");

        respuestaV1.StatusCode.Should().Be(HttpStatusCode.OK);
        respuestaV2.StatusCode.Should().Be(HttpStatusCode.OK);

        var cuerpoV1 = await respuestaV1.Content.ReadFromJsonAsync<ProductoResponseV1Dto>();
        var cuerpoV2 = await respuestaV2.Content.ReadFromJsonAsync<ProductoResponseV2Dto>();

        cuerpoV1!.Id.Should().Be(id);
        cuerpoV1.Nombre.Should().Be("Teclado versionado");

        cuerpoV2!.Id.Should().Be(id);
        cuerpoV2.Nombre.Should().Be("Teclado versionado");
        cuerpoV2.CreadoEnUtc.Should().NotBe(default(DateTime), "v2 agrega un campo que v1 nunca expuso, sin romper el contrato de v1");
    }

    [Fact]
    public async Task ObtenerProducto_VersionNoDeclarada_RetornaErrorClaro_NoSilencioso()
    {
        var id = await CrearProductoAsync("Producto cualquiera", 10m);

        var respuesta = await _client!.GetAsync($"/api/v99/productos/{id}");

        // Verificado contra el servidor real (no asumido): con UrlSegmentApiVersionReader +
        // AssumeDefaultVersionWhenUnspecified = false, el matching de versión ocurre a nivel de
        // selección de endpoint (Asp.Versioning descarta como candidatos los endpoints cuyo
        // ApiVersionSet no declara la versión "99"), no a través de un middleware de errores propio
        // -- al no quedar ningún endpoint candidato para esa combinación de ruta+versión, el
        // resultado es el 404 estándar de enrutamiento de ASP.NET Core, igual que pedir cualquier
        // otra ruta inexistente. Documentado explícitamente en docs/guia-versionado-api.md porque
        // difiere de lo asumido originalmente (400 "UnsupportedApiVersion"): ese código sí aplica al
        // caso "versión ausente cuando se exige explícita" o al pipeline de MVC con controllers, no
        // al de Minimal API con este único ApiVersionSet.
        respuesta.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ObtenerProducto_V1Deprecada_RespuestaIncluyeHeaderDeVersionesDeprecadas()
    {
        var id = await CrearProductoAsync("Producto deprecado", 5m);

        var respuesta = await _client!.GetAsync($"/api/v1/productos/{id}");

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
        respuesta.Headers.Should().ContainKey("api-deprecated-versions");
        respuesta.Headers.GetValues("api-deprecated-versions").Should().Contain(v => v.Contains('1'));
        respuesta.Headers.Should().ContainKey("api-supported-versions");
        respuesta.Headers.GetValues("api-supported-versions").Should().Contain(v => v.Contains('2'));
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

    private record ProductoResponseV1Dto(Guid Id, string Nombre, decimal Precio);

    private record ProductoResponseV2Dto(Guid Id, string Nombre, decimal Precio, DateTime CreadoEnUtc);
}
