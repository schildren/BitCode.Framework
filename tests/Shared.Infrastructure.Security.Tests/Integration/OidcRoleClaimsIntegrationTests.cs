using System.Net;
using System.Net.Http.Headers;
using BitCode.Framework.Shared.Infrastructure.Security.Oidc;
using BitCode.Framework.Shared.Infrastructure.Security.Permissions;
using BitCode.Framework.Shared.Testing;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BitCode.Framework.Shared.Infrastructure.Security.Tests.Integration;

/// <summary>
/// F2-07 (bugfix de correctitud): reproduce contra un Keycloak real (Testcontainers, ADR 0004) el gap
/// que ningún test unitario podía detectar (<see cref="PermissionEvaluatorTests"/> arma
/// <c>ClaimTypes.Role</c> a mano) -- un rol de REALM asignado de verdad en Keycloak, un token real
/// emitido para ese sujeto, y <c>[RequirePermission]</c>/<see cref="IPermissionEvaluator"/> resolviendo
/// el permiso a partir de ese rol, exactamente como lo vería un endpoint real de la API protegido por
/// <see cref="AddSharedOidcAuthentication"/>. Este test expuso, contra Keycloak real, DOS bugs
/// encadenados (ambos corregidos): (1) <c>realm_access.roles</c> nunca se aplanaba a
/// <c>ClaimTypes.Role</c> (resuelto por <c>OidcRoleClaimsTransformation</c>); (2) incluso ya resuelto
/// (1), <c>PermissionEvaluator</c> igual devolvía vacío porque el <c>"sub"</c> de Keycloak (un UUID) se
/// mapea automáticamente a <c>ClaimTypes.NameIdentifier</c> y el evaluador lo confundía con un userId
/// de Identity local, saltándose por completo la expansión por rol (ver el comentario de clase de
/// <see cref="Permissions.PermissionEvaluator"/>).
/// </summary>
[Collection(KeycloakCollection.Name)]
public class OidcRoleClaimsIntegrationTests(KeycloakContainerFixture fixture)
{
    private const string RoleName = "bitcode-integration-test-role";
    private const string ExpectedPermission = "productos.editar";

    // Rol que nunca se asigna en Keycloak durante este test -- deliberadamente distinto de RoleName
    // (que sí se asigna en Token_ConRolDeRealmAsignadoEnKeycloak_ConcedeElPermisoMapeadoAEseRol) para
    // que el control negativo sea determinista sin importar el orden de ejecución de los tests: ambos
    // comparten el mismo KeycloakCollection (y, con él, el mismo contenedor y la misma cuenta de
    // servicio) con OidcTokenValidationIntegrationTests, así que un rol ya asignado por OTRO test no
    // debe poder hacer pasar este control negativo por casualidad.
    private const string NeverAssignedRoleName = "bitcode-integration-test-role-never-assigned";
    private const string PermissionMappedToNeverAssignedRole = "productos.eliminar";

    /// <summary>
    /// Único puente entre "rol de Keycloak" y "permiso" para este test: en un proyecto solo-OIDC (sin
    /// <c>AddSharedSecurity</c>/Identity local), <c>NullPermissionService</c> nunca aporta permisos por
    /// rol a propósito (F2-07, ningún almacén de roles local) -- ese es justamente el caso de uso que
    /// <see cref="IPermissionEvaluator"/> resuelve por diseño (fuente "rol por claim del token"), así
    /// que probarlo de punta a punta contra Keycloak real exige un <see cref="IPermissionService"/> que
    /// sepa expandir ESE rol concreto, sin reintroducir Identity local (fuera de alcance de este bug).
    /// </summary>
    private sealed class RoleNameToPermissionService : IPermissionService
    {
        private static readonly Dictionary<string, string> RoleToPermission = new(StringComparer.Ordinal)
        {
            [RoleName] = ExpectedPermission,
            [NeverAssignedRoleName] = PermissionMappedToNeverAssignedRole,
        };

        public Task<IReadOnlyList<string>> GetPermissionsForUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<string>> GetPermissionsForRoleAsync(string roleName, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(RoleToPermission.TryGetValue(roleName, out var permission) ? [permission] : []);
    }

    private async Task<HttpClient> BuildApiClientProtectedByPermissionAsync(string requiredPermission)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Oidc:Authority"] = fixture.Authority,
                ["Oidc:Audience"] = KeycloakContainerFixture.ClientId,
                ["Oidc:RequireHttpsMetadata"] = "false",
            })
            .Build();

        var hostBuilder = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSharedOidcAuthentication(configuration);

                    // Reemplaza a NullPermissionService (registrado con TryAddScoped por
                    // AddSharedPermissionEvaluation) -- ver RoleNameToPermissionService.
                    services.AddScoped<IPermissionService, RoleNameToPermissionService>();
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/productos", () => Results.Ok("ok"))
                            .RequireAuthorization(requiredPermission);
                    });
                });
            });

        var host = await hostBuilder.StartAsync();
        return host.GetTestClient();
    }

    private static async Task<HttpStatusCode> CallProtectedEndpointAsync(HttpClient client, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/productos");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    [Fact]
    public async Task Token_ConRolDeRealmAsignadoEnKeycloak_ConcedeElPermisoMapeadoAEseRol()
    {
        await fixture.AssignRealmRoleToServiceAccountAsync(RoleName);
        using var client = await BuildApiClientProtectedByPermissionAsync(ExpectedPermission);
        var accessToken = await fixture.RequestAccessTokenAsync();

        var status = await CallProtectedEndpointAsync(client, accessToken);

        status.Should().Be(HttpStatusCode.OK,
            "un token real de Keycloak con el rol de realm asignado debe autorizar el permiso mapeado a ese " +
            "rol -- si OidcRoleClaimsTransformation no proyectara 'realm_access.roles' a ClaimTypes.Role, " +
            "PermissionEvaluator nunca encontraría el rol y este endpoint respondería 403");
    }

    [Fact]
    public async Task Token_SinElRolDeRealmAsignado_NoConcedeElPermiso()
    {
        // Control negativo: exige el permiso mapeado a NeverAssignedRoleName, que este test (a
        // propósito) nunca asigna en Keycloak -- determinista sin importar si
        // Token_ConRolDeRealmAsignadoEnKeycloak_ConcedeElPermisoMapeadoAEseRol (u otro test del mismo
        // KeycloakCollection) ya corrió antes sobre el mismo contenedor/cuenta de servicio compartidos.
        using var client = await BuildApiClientProtectedByPermissionAsync(PermissionMappedToNeverAssignedRole);
        var accessToken = await fixture.RequestAccessTokenAsync();

        var status = await CallProtectedEndpointAsync(client, accessToken);

        status.Should().Be(HttpStatusCode.Forbidden,
            "sin el rol de realm asignado en Keycloak, el permiso no debe concederse (default deny)");
    }
}
