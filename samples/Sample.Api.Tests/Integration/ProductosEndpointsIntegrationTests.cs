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

    [Fact]
    public async Task CrearYObtenerProducto_FlujoCompleto_PersisteYRespondeCorrectamente()
    {
        var crearResponse = await _client!.PostAsJsonAsync("/productos", new { nombre = "Teclado", precio = 49.90m });
        crearResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = await crearResponse.Content.ReadFromJsonAsync<Guid>();

        var obtenerResponse = await _client!.GetAsync($"/productos/{id}");
        obtenerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var producto = await obtenerResponse.Content.ReadFromJsonAsync<ProductoDto>();
        producto!.Nombre.Should().Be("Teclado");
    }

    [Fact]
    public async Task ObtenerProducto_Inexistente_Retorna404ConProblemDetails()
    {
        var response = await _client!.GetAsync($"/productos/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Producto.NoEncontrado");
    }

    [Fact]
    public async Task CrearProducto_DatosInvalidos_Retorna400ConErroresDeValidacion()
    {
        var response = await _client!.PostAsJsonAsync("/productos", new { nombre = "", precio = -5m });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Nombre").And.Contain("Precio");
    }

    [Fact]
    public async Task ListarProductos_ConPageSizeValido_RetornaPaginaCorrecta()
    {
        // F1-21: demuestra el mecanismo de paginación de punta a punta contra el endpoint real.
        await _client!.PostAsJsonAsync("/productos", new { nombre = "Mouse", precio = 15m });
        await _client!.PostAsJsonAsync("/productos", new { nombre = "Monitor", precio = 199m });

        var response = await _client!.GetAsync("/productos?page=1&pageSize=1");

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
        var response = await _client!.GetAsync("/productos?page=1&pageSize=10000");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Paginacion.TamanioExcedeLimite");
    }

    [Fact]
    public async Task ListarProductos_ConPageSizeCero_Retorna400ConErrorDeValidacion()
    {
        var response = await _client!.GetAsync("/productos?page=1&pageSize=0");

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
