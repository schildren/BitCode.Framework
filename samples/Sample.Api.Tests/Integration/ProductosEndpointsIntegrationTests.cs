using System.Net;
using System.Net.Http.Json;
using BitCode.Framework.Shared.Testing;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Sample.Api.Tests.Integration;

/// <summary>
/// Verifica el proyecto piloto de la Fase 8 de extremo a extremo contra un SQL Server real
/// (Testcontainers): Modularidad (Fase 5) + Persistencia (Fase 1) + Aplicación/MediatR (Fase 2) +
/// Web/ProblemDetails (Fase 4) funcionando juntos, tal como los usaría un consumidor real. Este
/// mismo flujo detectó en desarrollo que AddSharedPersistence no registraba IReadRepository&lt;,&gt;
/// (corregido en Shared.Infrastructure.Persistence, Fase 1).
///
/// Carpeta/namespace/nombre de clase con sufijo "Integration" por convención (ver
/// docs/convenciones.md, sección Testing): esta prueba usa <see cref="SqlServerContainerFixture"/>
/// (Testcontainers) y por lo tanto requiere Docker. Renombrada en F0-10 (línea base) tras detectar
/// que el nombre anterior (<c>Sample.Api.Tests.ProductosEndpointsTests</c>, sin "Integration") no
/// era excluido por el filtro de CI <c>FullyQualifiedName!~Integration</c>, lo que podía producir
/// falsos rojos en entornos/runners sin Docker.
/// </summary>
[Collection(SampleApiSequentialCollection.Name)]
public class ProductosEndpointsIntegrationTests : IAsyncLifetime
{
    private readonly SqlServerContainerFixture _sqlServerFixture = new();
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        await _sqlServerFixture.InitializeAsync();

        // Program.cs (patrón Minimal API) lee builder.Configuration de forma síncrona en
        // AddModules, antes de que WithWebHostBuilder/ConfigureAppConfiguration tenga oportunidad
        // de inyectar su override — la variable de entorno sí la recoge WebApplicationBuilder al
        // construirse, sin depender del orden de interceptación de WebApplicationFactory.
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__Default",
            _sqlServerFixture.BuildIsolatedConnectionString("SampleApiTests", nameof(ProductosEndpointsIntegrationTests)));

        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();
    }

    /// <summary>
    /// F1-22: CrearProductoCommand implementa IIdempotentCommand — toda creación real (fuera de los
    /// tests que ejercitan la idempotencia en sí misma) necesita una Idempotency-Key propia, o
    /// IdempotencyBehavior la rechaza con 400/"Idempotency.KeyRequired" (ver
    /// <see cref="CrearProducto_SinIdempotencyKey_Retorna400ConErrorDeValidacion"/>).
    /// </summary>
    private Task<HttpResponseMessage> PostProductoAsync(object payload, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/productos")
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString());

        return _client!.SendAsync(request);
    }

    [Fact]
    public async Task CrearYObtenerProducto_FlujoCompleto_PersisteYRespondeCorrectamente()
    {
        var crearResponse = await PostProductoAsync(new { nombre = "Teclado", precio = 49.90m });
        crearResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = await crearResponse.Content.ReadFromJsonAsync<Guid>();

        var obtenerResponse = await _client!.GetAsync($"/api/v1/productos/{id}");
        obtenerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var producto = await obtenerResponse.Content.ReadFromJsonAsync<ProductoDto>();
        producto!.Nombre.Should().Be("Teclado");
    }

    [Fact]
    public async Task ObtenerProducto_Inexistente_Retorna404ConProblemDetails()
    {
        var response = await _client!.GetAsync($"/api/v1/productos/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Producto.NoEncontrado");
    }

    [Fact]
    public async Task CrearProducto_DatosInvalidos_Retorna400ConErroresDeValidacion()
    {
        var response = await PostProductoAsync(new { nombre = "", precio = -5m });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Nombre").And.Contain("Precio");
    }

    /// <summary>
    /// Criterio de aceptación F1-22 ("POST repetido no duplica operación"), primer escenario:
    /// reenviar el MISMO comando (mismo body) con la MISMA Idempotency-Key no debe insertar un
    /// segundo Producto, y ambas respuestas deben ser idénticas (mismo 201 Created, mismo Guid) —
    /// como si el segundo POST nunca hubiera llegado al handler.
    /// </summary>
    [Fact]
    public async Task CrearProducto_MismaIdempotencyKeyYMismoPayload_NoDuplicaLaOperacionYRespondeIgual()
    {
        var idempotencyKey = Guid.NewGuid().ToString();
        var payload = new { nombre = "Teclado mecánico", precio = 89.90m };

        var primeraRespuesta = await PostProductoAsync(payload, idempotencyKey);
        var segundaRespuesta = await PostProductoAsync(payload, idempotencyKey);

        primeraRespuesta.StatusCode.Should().Be(HttpStatusCode.Created);
        segundaRespuesta.StatusCode.Should().Be(HttpStatusCode.Created);

        var primerId = await primeraRespuesta.Content.ReadFromJsonAsync<Guid>();
        var segundoId = await segundaRespuesta.Content.ReadFromJsonAsync<Guid>();
        segundoId.Should().Be(
            primerId,
            "el segundo POST con la misma Idempotency-Key debe devolver el mismo resultado ya " +
            "obtenido, sin volver a ejecutar el handler");

        var listado = await _client!.GetAsync("/api/v1/productos?page=1&pageSize=100");
        var pagina = await listado.Content.ReadFromJsonAsync<PagedResultDto>();
        pagina!.Items.Count(p => p.Nombre == "Teclado mecánico").Should().Be(
            1,
            "el efecto de negocio (crear el Producto) debe haber ocurrido UNA sola vez, no dos");
    }

    /// <summary>
    /// Criterio de aceptación F1-22 ("POST repetido no duplica operación"), segundo escenario:
    /// reenviar la MISMA Idempotency-Key con un body DISTINTO nunca se ejecuta como si fuera el
    /// mismo reintento — se rechaza con 409 Conflict/"Idempotency.KeyReused", y el segundo Producto
    /// nunca se crea (el listado solo contiene el primero).
    /// </summary>
    [Fact]
    public async Task CrearProducto_MismaIdempotencyKeyConPayloadDistinto_Retorna409YNoCreaElSegundoProducto()
    {
        var idempotencyKey = Guid.NewGuid().ToString();

        var primeraRespuesta = await PostProductoAsync(
            new { nombre = "Producto original", precio = 10m },
            idempotencyKey);
        var segundaRespuesta = await PostProductoAsync(
            new { nombre = "Producto distinto", precio = 999m },
            idempotencyKey);

        primeraRespuesta.StatusCode.Should().Be(HttpStatusCode.Created);
        segundaRespuesta.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await segundaRespuesta.Content.ReadAsStringAsync();
        body.Should().Contain("Idempotency.KeyReused");

        var listado = await _client!.GetAsync("/api/v1/productos?page=1&pageSize=100");
        var pagina = await listado.Content.ReadFromJsonAsync<PagedResultDto>();
        pagina!.Items.Should().NotContain(p => p.Nombre == "Producto distinto");
        pagina.Items.Count(p => p.Nombre == "Producto original").Should().Be(1);
    }

    /// <summary>
    /// Decisión F1-22 documentada en <c>IIdempotentCommand</c>/<c>docs/convenciones.md</c>: un
    /// comando marcado <c>IIdempotentCommand</c> exige una Idempotency-Key, nunca se ejecuta en
    /// silencio como si no fuera idempotente.
    /// </summary>
    [Fact]
    public async Task CrearProducto_SinIdempotencyKey_Retorna400ConErrorDeValidacion()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/productos")
        {
            Content = JsonContent.Create(new { nombre = "Sin clave", precio = 5m }),
        };

        var response = await _client!.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Idempotency.KeyRequired");
    }

    [Fact]
    public async Task ListarProductos_ConPageSizeValido_RetornaPaginaCorrecta()
    {
        // F1-21: demuestra el mecanismo de paginación de punta a punta contra el endpoint real.
        await PostProductoAsync(new { nombre = "Mouse", precio = 15m });
        await PostProductoAsync(new { nombre = "Monitor", precio = 199m });

        var response = await _client!.GetAsync("/api/v1/productos?page=1&pageSize=1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var pagina = await response.Content.ReadFromJsonAsync<PagedResultDto>();
        pagina!.Items.Should().HaveCount(1);
        pagina.PageSize.Should().Be(1);
        pagina.TotalCount.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task ListarProductos_ConPageSizeSuperiorAlMaximo_Retorna400ConErrorDeValidacion_SinTruncarEnSilencio()
    {
        // Criterio de aceptación F1-21: "ningún endpoint ilimitado". Pedir un pageSize
        // arbitrariamente alto debe fallar de forma explícita, nunca ejecutarse truncado en silencio
        // ni devolver todas las filas.
        var response = await _client!.GetAsync("/api/v1/productos?page=1&pageSize=10000");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Paginacion.TamanioExcedeLimite");
    }

    [Fact]
    public async Task ListarProductos_ConPageSizeCero_Retorna400ConErrorDeValidacion()
    {
        var response = await _client!.GetAsync("/api/v1/productos?page=1&pageSize=0");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Paginacion.TamanioInvalido");
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

    private record ProductoDto(Guid Id, string Nombre, decimal Precio);

    private record PagedResultDto(List<ProductoDto> Items, int Page, int PageSize, int TotalCount);
}
