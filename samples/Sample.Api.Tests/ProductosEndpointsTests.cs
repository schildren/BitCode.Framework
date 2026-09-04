using System.Net;
using System.Net.Http.Json;
using BitCode.Framework.Shared.Testing;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Sample.Api.Tests;

/// <summary>
/// Verifica el proyecto piloto de la Fase 8 de extremo a extremo contra un SQL Server real
/// (Testcontainers): Modularidad (Fase 5) + Persistencia (Fase 1) + Aplicación/MediatR (Fase 2) +
/// Web/ProblemDetails (Fase 4) funcionando juntos, tal como los usaría un consumidor real. Este
/// mismo flujo detectó en desarrollo que AddSharedPersistence no registraba IReadRepository&lt;,&gt;
/// (corregido en Shared.Infrastructure.Persistence, Fase 1).
/// </summary>
public class ProductosEndpointsTests : IAsyncLifetime
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
            _sqlServerFixture.BuildIsolatedConnectionString("SampleApiTests", nameof(ProductosEndpointsTests)));

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
}
