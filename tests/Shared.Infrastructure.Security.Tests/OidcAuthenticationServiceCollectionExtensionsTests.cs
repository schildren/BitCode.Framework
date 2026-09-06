using BitCode.Framework.Shared.Infrastructure.Security.Oidc;
using BitCode.Framework.Shared.Infrastructure.Security.Permissions;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests;

/// <summary>
/// F2-01: el adapter OIDC/OAuth2 debe quedar configurado exclusivamente a partir de la sección
/// "Oidc" (Authority/Audience) — estos tests verifican el contrato de registro (DI, esquema
/// "Bearer", validación de configuración obligatoria y que cambiar solo la configuración basta para
/// apuntar a un proveedor distinto), no el flujo de validación completo de tokens contra un IdP real
/// (eso es F2-05, con Keycloak vía Testcontainers).
/// </summary>
public class OidcAuthenticationServiceCollectionExtensionsTests
{
    private static IConfiguration BuildConfiguration(string authority, string audience) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Oidc:Authority"] = authority,
                ["Oidc:Audience"] = audience,
            })
            .Build();

    [Fact]
    public async Task AddSharedOidcAuthentication_RegistersBearerSchemeAsDefault()
    {
        var services = new ServiceCollection();
        services.AddSharedOidcAuthentication(BuildConfiguration("https://keycloak.local/realms/bitcode", "bitcode-api"));

        using var provider = services.BuildServiceProvider();
        var schemeProvider = provider.GetRequiredService<IAuthenticationSchemeProvider>();

        var defaultScheme = await schemeProvider.GetDefaultAuthenticateSchemeAsync();

        defaultScheme.Should().NotBeNull();
        defaultScheme!.Name.Should().Be(JwtBearerDefaults.AuthenticationScheme);
    }

    [Fact]
    public void AddSharedOidcAuthentication_ConfiguresAuthorityAndAudienceFromConfiguration_WithoutHardcodedProvider()
    {
        var services = new ServiceCollection();
        services.AddSharedOidcAuthentication(BuildConfiguration("https://keycloak.local/realms/bitcode", "bitcode-api"));

        using var provider = services.BuildServiceProvider();
        var jwtBearerOptions = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        jwtBearerOptions.Authority.Should().Be("https://keycloak.local/realms/bitcode");
        jwtBearerOptions.Audience.Should().Be("bitcode-api");
        jwtBearerOptions.TokenValidationParameters.ValidateIssuer.Should().BeTrue();
        jwtBearerOptions.TokenValidationParameters.ValidateAudience.Should().BeTrue();
        jwtBearerOptions.TokenValidationParameters.ValidateLifetime.Should().BeTrue();
        jwtBearerOptions.TokenValidationParameters.ValidateIssuerSigningKey.Should().BeTrue();
    }

    [Fact]
    public void AddSharedOidcAuthentication_DefaultsClockSkewToThirtySeconds_NotTheFrameworkDefaultOfFiveMinutes()
    {
        // F2-05: el framework fija explícitamente una tolerancia de reloj ajustada -- nunca debe
        // heredar en silencio el default de 5 minutos de TokenValidationParameters (ver OidcOptions.ClockSkew).
        var services = new ServiceCollection();
        services.AddSharedOidcAuthentication(BuildConfiguration("https://keycloak.local/realms/bitcode", "bitcode-api"));

        using var provider = services.BuildServiceProvider();
        var jwtBearerOptions = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        jwtBearerOptions.TokenValidationParameters.ClockSkew.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void AddSharedOidcAuthentication_RespectsClockSkewOverrideFromConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Oidc:Authority"] = "https://keycloak.local/realms/bitcode",
                ["Oidc:Audience"] = "bitcode-api",
                ["Oidc:ClockSkew"] = "00:02:00",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSharedOidcAuthentication(configuration);

        using var provider = services.BuildServiceProvider();
        var jwtBearerOptions = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        jwtBearerOptions.TokenValidationParameters.ClockSkew.Should().Be(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void AddSharedOidcAuthentication_SwappingConfigurationOnly_PointsToADifferentProvider()
    {
        // Demuestra el criterio de aceptación de F2-01 ("proveedor intercambiable por configuración"):
        // el mismo código de registro, con distinta configuración, apunta a un IdP OIDC diferente sin
        // ningún cambio de código (ej. pasar de un Keycloak self-hosted a Entra ID).
        var keycloakServices = new ServiceCollection();
        keycloakServices.AddSharedOidcAuthentication(BuildConfiguration("https://keycloak.local/realms/bitcode", "bitcode-api"));
        using var keycloakProvider = keycloakServices.BuildServiceProvider();

        var entraServices = new ServiceCollection();
        entraServices.AddSharedOidcAuthentication(
            BuildConfiguration("https://login.microsoftonline.com/tenant-id/v2.0", "api://bitcode-api"));
        using var entraProvider = entraServices.BuildServiceProvider();

        var keycloakOptions = keycloakProvider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        var entraOptions = entraProvider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        keycloakOptions.Authority.Should().Be("https://keycloak.local/realms/bitcode");
        entraOptions.Authority.Should().Be("https://login.microsoftonline.com/tenant-id/v2.0");
        keycloakOptions.Authority.Should().NotBe(entraOptions.Authority);
    }

    [Fact]
    public void AddSharedOidcAuthentication_RespectsMetadataAddressOverride()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Oidc:Authority"] = "https://keycloak.local/realms/bitcode",
                ["Oidc:Audience"] = "bitcode-api",
                ["Oidc:MetadataAddress"] = "https://keycloak.local/realms/bitcode/.well-known/openid-configuration-custom",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSharedOidcAuthentication(configuration);

        using var provider = services.BuildServiceProvider();
        var jwtBearerOptions = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        jwtBearerOptions.MetadataAddress.Should().Be(
            "https://keycloak.local/realms/bitcode/.well-known/openid-configuration-custom");
    }

    [Fact]
    public void AddSharedOidcAuthentication_DefaultsRequireHttpsMetadataToTrue()
    {
        var services = new ServiceCollection();
        services.AddSharedOidcAuthentication(BuildConfiguration("https://keycloak.local/realms/bitcode", "bitcode-api"));

        using var provider = services.BuildServiceProvider();
        var jwtBearerOptions = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        jwtBearerOptions.RequireHttpsMetadata.Should().BeTrue();
    }

    [Fact]
    public void AddSharedOidcAuthentication_WithoutOidcConfigurationSection_Throws()
    {
        var services = new ServiceCollection();
        var emptyConfiguration = new ConfigurationBuilder().Build();

        var act = () => services.AddSharedOidcAuthentication(emptyConfiguration);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddSharedOidcAuthentication_WithoutAudience_Throws()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Oidc:Authority"] = "https://keycloak.local/realms/bitcode",
            })
            .Build();

        var act = () => services.AddSharedOidcAuthentication(configuration);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddSharedOidcAuthentication_RegistersPermissionEvaluationWithoutRequiringAddSharedSecurity()
    {
        // F2-07 (RBAC 2.0): antes de esta tarea, AddSharedOidcAuthentication no registraba ningún
        // IAuthorizationPolicyProvider dinámico -- [RequirePermission]/RequireAuthorization("permiso")
        // nunca se resolvía bajo autenticación puramente OIDC (sin AddSharedSecurity/Identity local).
        var services = new ServiceCollection();
        services.AddSharedOidcAuthentication(BuildConfiguration("https://keycloak.local/realms/bitcode", "bitcode-api"));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IPermissionEvaluator>().Should().NotBeNull();
        provider.GetRequiredService<IPermissionService>().Should().BeOfType<NullPermissionService>();
        provider.GetRequiredService<IAuthorizationPolicyProvider>().Should().BeOfType<PermissionAuthorizationPolicyProvider>();
        provider.GetServices<IAuthorizationHandler>().Should().Contain(h => h is PermissionAuthorizationHandler);
    }
}
