using System.Net.Http.Json;
using BitCode.Framework.Shared.Infrastructure.Security.Secrets;
using BitCode.Framework.Shared.Testing;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Integration;

/// <summary>
/// F2-12, criterio de aceptación "Abstracción y provider": ejercita <see cref="VaultSecretProvider"/>
/// de punta a punta contra un HashiCorp Vault real (Testcontainers, modo dev, ADR 0014) -- issuer/token
/// de acceso reales, no un <see cref="HttpMessageHandler"/> falso (eso ya lo cubren las pruebas
/// unitarias, <c>Secrets/VaultSecretProviderTests.cs</c>). Escribe cada secreto directamente contra la
/// API KV v2 de Vault (mismo motor y ruta que <see cref="VaultSecretProvider"/> lee) antes de leerlo a
/// través de <see cref="AddSharedSecretProvider"/>, para no acoplar la prueba a ningún detalle interno
/// del provider.
/// </summary>
[Collection(VaultCollection.Name)]
public class VaultSecretProviderIntegrationTests(VaultContainerFixture fixture)
{
    private ISecretProvider BuildProvider(string token)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Secrets:Provider"] = "Vault",
                ["Secrets:Vault:Address"] = fixture.Address,
                ["Secrets:Vault:Token"] = token,
                ["Secrets:Vault:PathPrefix"] = "bitcode-tests",
                // Vault en modo dev (Testcontainers) no expone TLS -- ver el comentario de
                // VaultContainerFixture.Address.
                ["Secrets:Vault:AllowInsecureHttp"] = "true",
            })
            .Build();

        services.AddSharedSecretProvider(configuration);
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<ISecretProvider>();
    }

    private async Task WriteSecretAsync(string key, string value)
    {
        using var httpClient = new HttpClient { BaseAddress = new Uri(fixture.Address) };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"v1/secret/data/bitcode-tests/{key}")
        {
            Content = JsonContent.Create(new { data = new Dictionary<string, string> { ["value"] = value } }),
        };
        request.Headers.Add("X-Vault-Token", VaultContainerFixture.RootToken);

        using var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task GetSecretAsync_WithSecretWrittenToVault_ReturnsSuccessWithTheStoredValue()
    {
        var key = $"connection-string-{Guid.NewGuid():N}";
        await WriteSecretAsync(key, "Server=sql;Database=bitcode;");

        var sut = BuildProvider(VaultContainerFixture.RootToken);
        var result = await sut.GetSecretAsync(key);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Server=sql;Database=bitcode;");
    }

    [Fact]
    public async Task GetSecretAsync_WithKeyThatWasNeverWritten_ReturnsFailureNotFound()
    {
        var sut = BuildProvider(VaultContainerFixture.RootToken);

        var result = await sut.GetSecretAsync($"nunca-existio-{Guid.NewGuid():N}");

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Secrets.NotFound");
    }

    [Fact]
    public async Task GetSecretAsync_WithInvalidToken_ReturnsFailureAccessDenied()
    {
        var key = $"secreto-protegido-{Guid.NewGuid():N}";
        await WriteSecretAsync(key, "valor");

        var sut = BuildProvider("token-invalido-no-reconocido-por-vault");
        var result = await sut.GetSecretAsync(key);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Secrets.AccessDenied");
    }

    [Fact]
    public async Task GetSecretAsync_TwoDifferentKeys_ResolveIndependently()
    {
        // No hay ningún cacheo propio en VaultSecretProvider (a diferencia de ServiceTokenProvider,
        // F2-04) -- cada llamada resuelve el valor vigente en Vault en ese instante.
        var keyA = $"clave-a-{Guid.NewGuid():N}";
        var keyB = $"clave-b-{Guid.NewGuid():N}";
        await WriteSecretAsync(keyA, "valor-a");
        await WriteSecretAsync(keyB, "valor-b");

        var sut = BuildProvider(VaultContainerFixture.RootToken);

        (await sut.GetSecretAsync(keyA)).Value.Should().Be("valor-a");
        (await sut.GetSecretAsync(keyB)).Value.Should().Be("valor-b");
    }
}
