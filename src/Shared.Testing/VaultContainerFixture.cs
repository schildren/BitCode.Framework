using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace BitCode.Framework.Shared.Testing;

/// <summary>
/// Fixture de xUnit reutilizable para pruebas de integración del proveedor de secretos Vault (F2-12)
/// contra un HashiCorp Vault real (ADR 0014) mediante Testcontainers. Arranca el contenedor oficial
/// <c>hashicorp/vault</c> en modo "dev" (<c>VAULT_DEV_ROOT_TOKEN_ID</c> fijo, sin TLS, sin sellado) -- el
/// modo estándar de Vault para pruebas locales/CI, análogo al <c>--import-realm</c> de
/// <see cref="KeycloakContainerFixture"/>: no representa una configuración productiva (Vault productivo
/// nunca corre en modo dev, con auto-unseal deshabilitado y el root token fijo conocido de antemano).
/// </summary>
public sealed class VaultContainerFixture : IAsyncLifetime
{
    /// <summary>
    /// Root token del Vault de modo dev. No es un secreto real: existe únicamente dentro de un contenedor
    /// efímero (Testcontainers) descartado al finalizar la prueba -- mismo razonamiento documentado en
    /// <see cref="KeycloakContainerFixture.ClientSecret"/> para el secreto de cliente de prueba de Keycloak.
    /// </summary>
    public const string RootToken = "bitcode-test-root-token";

    private const int VaultPort = 8200;

    private readonly IContainer _container = new ContainerBuilder()
        .WithImage("hashicorp/vault:1.15")
        .WithPortBinding(VaultPort, assignRandomHostPort: true)
        .WithEnvironment("VAULT_DEV_ROOT_TOKEN_ID", RootToken)
        .WithEnvironment("VAULT_DEV_LISTEN_ADDRESS", $"0.0.0.0:{VaultPort}")
        .WithWaitStrategy(Wait.ForUnixContainer()
            .UntilHttpRequestIsSucceeded(request => request
                .ForPort(VaultPort)
                .ForPath("/v1/sys/health")))
        .Build();

    /// <summary>
    /// Dirección HTTP base del Vault de prueba (sin TLS -- Vault en modo dev no soporta HTTPS; por eso
    /// las pruebas contra este fixture configuran explícitamente <c>VaultSecretProviderOptions.AllowInsecureHttp = true</c>,
    /// nunca en un entorno real).
    /// </summary>
    public string Address => $"http://{_container.Hostname}:{_container.GetMappedPublicPort(VaultPort)}";

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
