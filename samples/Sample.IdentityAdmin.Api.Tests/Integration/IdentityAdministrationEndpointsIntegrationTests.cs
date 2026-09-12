using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BitCode.Framework.Platform.Identity;
using BitCode.Framework.Shared.Infrastructure.Security.Identity;
using BitCode.Framework.Shared.Infrastructure.Security.Jwt;
using BitCode.Framework.Shared.Infrastructure.Security.Permissions;
using BitCode.Framework.Shared.Testing;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Sample.IdentityAdmin.Api.Tests.Integration;

/// <summary>
/// Verifica de punta a punta, contra SQL Server real (Testcontainers), el módulo Identity
/// Administration (Fase 6, módulo 1): alta de usuario, alta de rol, concesión de permiso, asignación
/// de rol a usuario (RBAC con permiso distinto al de lectura + ABAC de no-autoasignación), listado/
/// revocación de sesiones, desactivación de usuario e idempotencia de las mutaciones expuestas por
/// API. No hay endpoint de login en este módulo (Identity Administration administra identidades, no
/// emite tokens -- eso es Security 2.0/Fase 2): los tests generan el JWT directamente vía
/// <see cref="IJwtTokenGenerator"/>, mismo patrón que
/// <c>Shared.Infrastructure.Security.Tests/Integration/SecurityEndToEndTests.cs</c>.
/// </summary>
public class IdentityAdministrationEndpointsIntegrationTests : IAsyncLifetime
{
    private readonly SqlServerContainerFixture _sqlServerFixture = new();
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        await _sqlServerFixture.InitializeAsync();

        Environment.SetEnvironmentVariable(
            "ConnectionStrings__Default",
            _sqlServerFixture.BuildIsolatedConnectionString("SampleIdentityAdminApiTests", Guid.NewGuid().ToString("N")));

        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();
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

    /// <summary>
    /// Crea un usuario administrador con TODOS los permisos que este test suite ejercita y devuelve
    /// el JWT correspondiente -- infraestructura de test, no parte del módulo (Identity Administration
    /// no emite tokens, ver el resumen de la clase).
    /// </summary>
    private async Task<(Guid UserId, string Token)> SeedActorAsync(string userName, params string[] permissions)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        var tokenGenerator = scope.ServiceProvider.GetRequiredService<IJwtTokenGenerator>();

        var roleName = $"rol-{userName}";
        var role = new ApplicationRole(roleName);
        (await roleManager.CreateAsync(role)).Succeeded.Should().BeTrue();

        foreach (var permission in permissions)
        {
            (await roleManager.AddPermissionAsync(role, permission)).Succeeded.Should().BeTrue();
        }

        var user = new ApplicationUser { UserName = userName, Email = $"{userName}@test.local" };
        (await userManager.CreateAsync(user, "Contraseña!Segura1")).Succeeded.Should().BeTrue();
        (await userManager.AddToRoleAsync(user, roleName)).Succeeded.Should().BeTrue();

        var token = tokenGenerator.GenerateAccessToken(user, [roleName], []);
        return (user.Id, token);
    }

    /// <summary>
    /// Todas las mutaciones expuestas por este módulo implementan <c>IIdempotentCommand</c> (F1-22) --
    /// un GET nunca necesita la clave, así que se genera una automáticamente para cualquier otro verbo
    /// salvo que el llamador pase una explícita (por ejemplo, para el test de idempotencia en sí, que
    /// reutiliza la MISMA clave a propósito en dos requests).
    /// </summary>
    private HttpRequestMessage BuildRequest(HttpMethod method, string url, string token, object? body = null, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (method != HttpMethod.Get)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString());
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    [Fact]
    public async Task CrearUsuario_SinAutenticacion_Retorna401()
    {
        var response = await _client!.PostAsJsonAsync(
            "/api/v1/identidad/usuarios",
            new { userName = "sinauth", email = "sinauth@test.local", password = "Contraseña!Segura1" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CrearUsuario_ConPermiso_PersisteYEsIdempotente()
    {
        var (_, adminToken) = await SeedActorAsync("admin-crea", IdentityAdministrationPermissions.UsuariosCrear);
        var idempotencyKey = Guid.NewGuid().ToString();
        var payload = new { userName = "nuevo.usuario", email = "nuevo.usuario@test.local", password = "Contraseña!Segura1" };

        var primeraRespuesta = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, "/api/v1/identidad/usuarios", adminToken, payload, idempotencyKey));
        primeraRespuesta.StatusCode.Should().Be(HttpStatusCode.Created);
        var primerId = await primeraRespuesta.Content.ReadFromJsonAsync<Guid>();

        // F1-22: mismo Idempotency-Key + mismo body -> mismo resultado, sin crear un segundo usuario.
        var segundaRespuesta = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, "/api/v1/identidad/usuarios", adminToken, payload, idempotencyKey));
        var segundoId = await segundaRespuesta.Content.ReadFromJsonAsync<Guid>();
        segundoId.Should().Be(primerId);
    }

    [Fact]
    public async Task ObtenerUsuario_SinPermiso_Retorna403()
    {
        var (targetId, _) = await SeedActorAsync("objetivo1", IdentityAdministrationPermissions.UsuariosVer);
        var (_, tokenSinPermiso) = await SeedActorAsync("sin-permiso");

        var response = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/identidad/usuarios/{targetId}", tokenSinPermiso));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AsignarRol_AOtroUsuario_ConPermiso_Permite()
    {
        var (adminId, adminToken) = await SeedActorAsync(
            "admin-asigna",
            IdentityAdministrationPermissions.UsuariosRolesAsignar,
            IdentityAdministrationPermissions.RolesCrear,
            IdentityAdministrationPermissions.UsuariosVer);
        var (otroUsuarioId, _) = await SeedActorAsync("otro-usuario");

        var crearRolResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, "/api/v1/identidad/roles", adminToken, new { nombreRol = "Ventas" }));
        crearRolResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var asignarResponse = await _client!.SendAsync(
            BuildRequest(
                HttpMethod.Post,
                $"/api/v1/identidad/usuarios/{otroUsuarioId}/roles",
                adminToken,
                new { nombreRol = "Ventas" }));

        asignarResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var obtenerResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/identidad/usuarios/{otroUsuarioId}", adminToken));
        var usuario = await obtenerResponse.Content.ReadFromJsonAsync<UsuarioResponseDto>();
        usuario!.Roles.Should().Contain("Ventas");

        adminId.Should().NotBe(otroUsuarioId);
    }

    /// <summary>
    /// El caso de referencia del requisito común "RBAC y ABAC en operaciones sensibles" (Fase 6): el
    /// actor TIENE el permiso RBAC (<see cref="IdentityAdministrationPermissions.UsuariosRolesAsignar"/>)
    /// pero la regla ABAC <c>SelfRoleAssignmentAbacRule</c> deniega la auto-asignación de todos modos.
    /// </summary>
    [Fact]
    public async Task AsignarRol_ASiMismo_EsDenegadoPorAbacAunqueTengaElPermisoRbac()
    {
        var (adminId, adminToken) = await SeedActorAsync(
            "admin-autoasigna",
            IdentityAdministrationPermissions.UsuariosRolesAsignar,
            IdentityAdministrationPermissions.RolesCrear);

        await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, "/api/v1/identidad/roles", adminToken, new { nombreRol = "Soporte" }));

        var response = await _client!.SendAsync(
            BuildRequest(
                HttpMethod.Post,
                $"/api/v1/identidad/usuarios/{adminId}/roles",
                adminToken,
                new { nombreRol = "Soporte" }));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DesactivarUsuario_ConPermiso_BloqueaElLogin()
    {
        var (adminId, adminToken) = await SeedActorAsync(
            "admin-desactiva",
            IdentityAdministrationPermissions.UsuariosDesactivar,
            IdentityAdministrationPermissions.UsuariosVer);
        var (targetId, _) = await SeedActorAsync("a-desactivar");

        var desactivarResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, $"/api/v1/identidad/usuarios/{targetId}/desactivar", adminToken));
        desactivarResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var obtenerResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/identidad/usuarios/{targetId}", adminToken));
        var usuario = await obtenerResponse.Content.ReadFromJsonAsync<UsuarioResponseDto>();
        usuario!.Bloqueado.Should().BeTrue();

        adminId.Should().NotBe(targetId);
    }

    [Fact]
    public async Task ListarYRevocarSesion_ConPermiso_FuncionaDePuntaAPunta()
    {
        var (_, adminToken) = await SeedActorAsync(
            "admin-sesiones",
            IdentityAdministrationPermissions.SesionesVer,
            IdentityAdministrationPermissions.SesionesRevocar);
        var (targetId, _) = await SeedActorAsync("con-sesion");

        // Identity Administration no emite refresh tokens (eso es Security 2.0/login, fuera de
        // alcance de este módulo) -- se siembra una sesión activa directamente, como lo haría un
        // login real en un consumidor completo.
        Guid sessionId;
        await using (var scope = _factory!.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityAdministrationDbContext>();
            var refreshToken = new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = targetId,
                Token = Guid.NewGuid().ToString(),
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.AddDays(7),
            };
            dbContext.RefreshTokens.Add(refreshToken);
            await dbContext.SaveChangesAsync();
            sessionId = refreshToken.Id;
        }

        var listarResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/identidad/usuarios/{targetId}/sesiones", adminToken));
        listarResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var sesiones = await listarResponse.Content.ReadFromJsonAsync<List<SesionResponseDto>>();
        sesiones.Should().ContainSingle(s => s.Id == sessionId);

        var revocarResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, $"/api/v1/identidad/sesiones/{sessionId}/revocar", adminToken));
        revocarResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var listarDespuesResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, $"/api/v1/identidad/usuarios/{targetId}/sesiones", adminToken));
        var sesionesDespues = await listarDespuesResponse.Content.ReadFromJsonAsync<List<SesionResponseDto>>();
        sesionesDespues.Should().BeEmpty();
    }

    [Fact]
    public async Task AsignarPermisoARol_YQuitarPermiso_FuncionaDePuntaAPunta()
    {
        var (_, adminToken) = await SeedActorAsync(
            "admin-permisos",
            IdentityAdministrationPermissions.RolesCrear,
            IdentityAdministrationPermissions.RolesPermisosAdministrar,
            IdentityAdministrationPermissions.RolesVer);

        await _client!.SendAsync(
            BuildRequest(HttpMethod.Post, "/api/v1/identidad/roles", adminToken, new { nombreRol = "Auditores" }));

        var asignarPermisoResponse = await _client!.SendAsync(
            BuildRequest(
                HttpMethod.Post,
                "/api/v1/identidad/roles/Auditores/permisos",
                adminToken,
                new { permiso = "identidad.usuarios.ver" }));
        asignarPermisoResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var listarRolesResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, "/api/v1/identidad/roles", adminToken));
        var roles = await listarRolesResponse.Content.ReadFromJsonAsync<List<RolResponseDto>>();
        roles.Should().Contain(r => r.NombreRol == "Auditores" && r.Permisos.Contains("identidad.usuarios.ver"));

        var quitarPermisoResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Delete, "/api/v1/identidad/roles/Auditores/permisos/identidad.usuarios.ver", adminToken));
        quitarPermisoResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var listarRolesDespuesResponse = await _client!.SendAsync(
            BuildRequest(HttpMethod.Get, "/api/v1/identidad/roles", adminToken));
        var rolesDespues = await listarRolesDespuesResponse.Content.ReadFromJsonAsync<List<RolResponseDto>>();
        rolesDespues.Should().Contain(r => r.NombreRol == "Auditores" && r.Permisos.Count == 0);
    }

    private sealed record UsuarioResponseDto(Guid Id, string? UserName, string? Email, bool Bloqueado, List<string> Roles);

    private sealed record SesionResponseDto(Guid Id, DateTime CreadaEnUtc, DateTime ExpiraEnUtc);

    private sealed record RolResponseDto(Guid Id, string NombreRol, List<string> Permisos);
}
