using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Testcontainers.Keycloak;
using Xunit;

namespace BitCode.Framework.Shared.Testing;

/// <summary>
/// Fixture de xUnit reutilizable para pruebas de integración de validación de tokens (F2-05) contra
/// un IdP OIDC real (Keycloak, ADR 0004) mediante Testcontainers. Importa al arrancar un realm de
/// prueba mínimo ("bitcode-test") con un cliente confidencial habilitado para Client Credentials —
/// suficiente para emitir tokens de acceso reales firmados por Keycloak sin necesitar un usuario ni
/// un flujo interactivo, que es todo lo que necesita <c>OidcAuthenticationServiceCollectionExtensions</c>
/// para validar issuer/audience/firma/vigencia (no ejercita Authorization Code + PKCE, eso ya lo cubre
/// F2-02 con mocks del endpoint de token).
/// </summary>
public sealed class KeycloakContainerFixture : IAsyncLifetime
{
    /// <summary>Nombre del realm de prueba importado al arrancar el contenedor.</summary>
    public const string RealmName = "bitcode-test";

    /// <summary>
    /// Segundo realm de prueba, independiente del primero (issuer, claves de firma y cliente propios),
    /// usado exclusivamente para el caso negativo "issuer incorrecto": un token real y con firma válida
    /// -- pero emitido por un realm distinto al configurado en <see cref="Authority"/> -- debe seguir
    /// siendo rechazado. Fijar <c>TokenValidationParameters.ValidIssuer</c> a mano no sirve para aislar
    /// este caso porque <c>JwtBearerHandler</c> siempre concatena el issuer real resuelto por OIDC
    /// Discovery a los issuers válidos -- la única forma realista de probar un issuer incorrecto es un
    /// token que efectivamente venga de otro emisor.
    /// </summary>
    public const string OtherRealmName = "bitcode-test-other";

    /// <summary>Client id del cliente confidencial de prueba (también usado como audience esperada).</summary>
    public const string ClientId = "bitcode-api";

    /// <summary>Client id del cliente confidencial del segundo realm (<see cref="OtherRealmName"/>).</summary>
    public const string OtherRealmClientId = "bitcode-api-other";

    /// <summary>
    /// Secreto del cliente de prueba. No es un secreto real: existe únicamente dentro de un contenedor
    /// Keycloak efímero (Testcontainers) que se descarta al finalizar la prueba y nunca se expone fuera
    /// de este proceso — no viola la prohibición de guardar secretos en el repositorio (docs/convenciones.md)
    /// de la misma manera que la contraseña "sa" de <see cref="SqlServerContainerFixture"/> tampoco lo hace.
    /// </summary>
    public const string ClientSecret = "bitcode-test-client-secret";

    /// <summary>
    /// Vigencia del access token (segundos) configurada en el realm de prueba. Deliberadamente corta
    /// para que las pruebas de expiración y de clock skew (F2-05) no necesiten esperas largas.
    /// </summary>
    public const int AccessTokenLifespanSeconds = 5;

    /// <summary>Usuario/contraseña del admin del realm "master", fijados explícitamente (F2-06) en vez de
    /// depender del default de <c>Testcontainers.Keycloak</c> -- los necesita
    /// <see cref="RotateSigningKeyAsync"/> para autenticarse contra la Admin REST API y simular una
    /// rotación real de claves de firma.</summary>
    private const string AdminUsername = "admin";
    private const string AdminPassword = "admin";

    private readonly KeycloakContainer _container = new KeycloakBuilder()
        .WithUsername(AdminUsername)
        .WithPassword(AdminPassword)
        .WithResourceMapping(Encoding.UTF8.GetBytes(RealmExportJson), "/opt/keycloak/data/import/realm.json")
        .WithResourceMapping(Encoding.UTF8.GetBytes(OtherRealmExportJson), "/opt/keycloak/data/import/realm-other.json")
        .WithCommand("--import-realm")
        .Build();

    /// <summary>Issuer ("iss") real del realm de prueba, resuelto por Keycloak Discovery.</summary>
    public string Authority => $"{_container.GetBaseAddress()}/realms/{RealmName}";

    /// <summary>Issuer del segundo realm de prueba (ver <see cref="OtherRealmName"/>).</summary>
    public string OtherRealmAuthority => $"{_container.GetBaseAddress()}/realms/{OtherRealmName}";

    /// <summary>
    /// Solicita un access token real emitido por Keycloak (Client Credentials) para el cliente de
    /// prueba. Cada llamada emite un token nuevo (vigencia completa desde ese instante) — usar para el
    /// caso positivo y para los casos negativos que no dependen de la vigencia (firma alterada, audience
    /// incorrecta).
    /// </summary>
    public Task<string> RequestAccessTokenAsync(CancellationToken cancellationToken = default) =>
        RequestAccessTokenAsync(Authority, ClientId, cancellationToken);

    /// <summary>
    /// Solicita un access token real emitido por el segundo realm de prueba (<see cref="OtherRealmName"/>)
    /// -- mismo formato de token, pero con un "iss" y unas claves de firma distintas a las de
    /// <see cref="Authority"/>. Usar exclusivamente para el caso negativo "issuer incorrecto".
    /// </summary>
    public Task<string> RequestAccessTokenFromOtherRealmAsync(CancellationToken cancellationToken = default) =>
        RequestAccessTokenAsync(OtherRealmAuthority, OtherRealmClientId, cancellationToken);

    private async Task<string> RequestAccessTokenAsync(string authority, string clientId, CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient();
        using var response = await httpClient.PostAsync(
            $"{authority}/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = clientId,
                ["client_secret"] = ClientSecret,
            }),
            cancellationToken);

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        return payload.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Keycloak no devolvió 'access_token' en la respuesta del token endpoint.");
    }

    /// <summary>
    /// Rota la clave de firma activa del realm de prueba (F2-06, "Rotación sin downtime"): genera un
    /// nuevo par de claves RSA vía la Admin REST API de Keycloak (<c>POST /admin/realms/{realm}/components</c>,
    /// <c>providerId=rsa-generated</c>) con prioridad más alta que la clave por defecto del realm --
    /// exactamente lo que un operador real haría para rotar la clave de firma sin dar de baja el realm.
    /// A partir de esta llamada, los tokens NUEVOS que Keycloak emita quedan firmados con la clave nueva
    /// (<c>kid</c> distinto del que ya pudo resolver <c>ConfigurationManager&lt;OpenIdConnectConfiguration&gt;</c>
    /// antes de esta rotación) -- el escenario real que <c>OidcOptions.RefreshOnIssuerKeyNotFound</c>
    /// (F2-06) debe absorber sin que la aplicación consumidora se reinicie.
    /// </summary>
    public Task RotateSigningKeyAsync(CancellationToken cancellationToken = default) =>
        RotateSigningKeyAsync(RealmName, cancellationToken);

    /// <summary>
    /// Obtiene un access token del usuario admin del realm "master" (Admin REST API de Keycloak) --
    /// método compartido por toda operación de administración de este fixture (rotación de clave,
    /// F2-06; alta de rol de realm y asignación a un sujeto, bugfix F2-07) para no duplicar el flujo de
    /// autenticación contra "master" en cada una.
    /// </summary>
    private async Task<string> GetAdminAccessTokenAsync(HttpClient httpClient, CancellationToken cancellationToken)
    {
        using var tokenResponse = await httpClient.PostAsync(
            $"{_container.GetBaseAddress()}/realms/master/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "admin-cli",
                ["username"] = AdminUsername,
                ["password"] = AdminPassword,
            }),
            cancellationToken);
        tokenResponse.EnsureSuccessStatusCode();
        var tokenPayload = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        return tokenPayload.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Keycloak no devolvió 'access_token' para el usuario admin.");
    }

    /// <summary>
    /// Crea un rol de realm nuevo (si no existe todavía) y lo asigna a la cuenta de servicio del
    /// cliente de prueba (<see cref="ClientId"/>) -- el "usuario" real, del lado de Keycloak, detrás de
    /// un token de Client Credentials (Service Accounts). A partir de esta llamada, un token nuevo
    /// emitido para <see cref="ClientId"/> (<see cref="RequestAccessTokenAsync()"/>) trae el rol en
    /// <c>realm_access.roles</c> (bugfix F2-07, ver <c>OidcRoleClaimsTransformation</c>): es la única
    /// forma realista de probar contra Keycloak real que un rol de realm efectivamente asignado en el
    /// IdP se proyecta y autoriza de punta a punta, sin mockear la forma del token.
    /// </summary>
    public async Task AssignRealmRoleToServiceAccountAsync(string roleName, CancellationToken cancellationToken = default)
    {
        using var httpClient = new HttpClient();
        var adminAccessToken = await GetAdminAccessTokenAsync(httpClient, cancellationToken);

        HttpRequestMessage AuthorizedRequest(HttpMethod method, string relativeUrl) =>
            new(method, $"{_container.GetBaseAddress()}{relativeUrl}")
            {
                Headers = { Authorization = new AuthenticationHeaderValue("Bearer", adminAccessToken) },
            };

        // 1) Crear el rol de realm (idempotente: 409 si ya existe, en cuyo caso seguimos igual).
        using var createRoleRequest = AuthorizedRequest(HttpMethod.Post, $"/admin/realms/{RealmName}/roles");
        createRoleRequest.Content = JsonContent.Create(new { name = roleName });
        using var createRoleResponse = await httpClient.SendAsync(createRoleRequest, cancellationToken);
        if (createRoleResponse.StatusCode != HttpStatusCode.Conflict)
        {
            createRoleResponse.EnsureSuccessStatusCode();
        }

        // 2) Resolver la representación completa del rol (id + name), que la Admin REST API exige tal
        // cual para asignarlo -- no alcanza con el nombre.
        using var getRoleRequest = AuthorizedRequest(HttpMethod.Get, $"/admin/realms/{RealmName}/roles/{roleName}");
        using var getRoleResponse = await httpClient.SendAsync(getRoleRequest, cancellationToken);
        getRoleResponse.EnsureSuccessStatusCode();
        var roleRepresentation = await getRoleResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);

        // 3) Resolver el id interno (UUID) del cliente de prueba a partir de su clientId público.
        using var getClientsRequest = AuthorizedRequest(
            HttpMethod.Get,
            $"/admin/realms/{RealmName}/clients?clientId={ClientId}");
        using var getClientsResponse = await httpClient.SendAsync(getClientsRequest, cancellationToken);
        getClientsResponse.EnsureSuccessStatusCode();
        var clients = await getClientsResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        var clientUuid = clients.EnumerateArray().First().GetProperty("id").GetString()
            ?? throw new InvalidOperationException($"No se encontró el cliente '{ClientId}' en el realm '{RealmName}'.");

        // 4) Resolver el usuario (service account) que Keycloak crea automáticamente para el cliente
        // confidencial -- es el "sujeto" real de un token de Client Credentials.
        using var getServiceAccountRequest = AuthorizedRequest(
            HttpMethod.Get,
            $"/admin/realms/{RealmName}/clients/{clientUuid}/service-account-user");
        using var getServiceAccountResponse = await httpClient.SendAsync(getServiceAccountRequest, cancellationToken);
        getServiceAccountResponse.EnsureSuccessStatusCode();
        var serviceAccountUser = await getServiceAccountResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        var serviceAccountUserId = serviceAccountUser.GetProperty("id").GetString()
            ?? throw new InvalidOperationException($"No se pudo resolver la cuenta de servicio del cliente '{ClientId}'.");

        // 5) Asignar el rol de realm a esa cuenta de servicio.
        using var assignRoleRequest = AuthorizedRequest(
            HttpMethod.Post,
            $"/admin/realms/{RealmName}/users/{serviceAccountUserId}/role-mappings/realm");
        assignRoleRequest.Content = JsonContent.Create(new[]
        {
            new { id = roleRepresentation.GetProperty("id").GetString(), name = roleRepresentation.GetProperty("name").GetString() },
        });
        using var assignRoleResponse = await httpClient.SendAsync(assignRoleRequest, cancellationToken);
        assignRoleResponse.EnsureSuccessStatusCode();
    }

    private async Task RotateSigningKeyAsync(string realm, CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient();
        var adminAccessToken = await GetAdminAccessTokenAsync(httpClient, cancellationToken);

        using var createKeyRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_container.GetBaseAddress()}/admin/realms/{realm}/components")
        {
            Content = JsonContent.Create(new
            {
                name = $"bitcode-rotated-key-{Guid.NewGuid():N}",
                providerId = "rsa-generated",
                providerType = "org.keycloak.keys.KeyProvider",
                parentId = realm,
                config = new Dictionary<string, string[]>
                {
                    // Prioridad más alta que la clave "rsa-generated" que Keycloak crea por defecto
                    // para todo realm nuevo (prioridad 100) -- la convierte en la clave ACTIVA de firma:
                    // los próximos tokens emitidos por este realm usan esta clave nueva, no la anterior.
                    ["priority"] = ["200"],
                    ["enabled"] = ["true"],
                    ["active"] = ["true"],
                    ["algorithm"] = ["RS256"],
                    ["keySize"] = ["2048"],
                },
            }),
        };
        createKeyRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminAccessToken);

        using var createKeyResponse = await httpClient.SendAsync(createKeyRequest, cancellationToken);
        createKeyResponse.EnsureSuccessStatusCode();
    }

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>
    /// Realm export mínimo: un cliente confidencial ("bitcode-api") con Service Accounts habilitado
    /// (Client Credentials) y un mapper de audience que fija "aud" al propio client id, para que
    /// coincida exactamente con <see cref="ClientId"/> y sea comparable contra
    /// <c>OidcOptions.Audience</c> en las pruebas. También declara explícitamente el protocol mapper de
    /// roles de realm ("realm_access.roles", el mismo que trae por defecto el client scope incorporado
    /// "roles" de Keycloak) -- necesario porque este realm export NO referencia ningún
    /// <c>defaultClientScopes</c> built-in (el import parcial no los adjunta automáticamente al
    /// cliente), y <see cref="AssignRealmRoleToServiceAccountAsync"/> (bugfix F2-07) depende de que un
    /// rol de realm asignado a la cuenta de servicio efectivamente aparezca en el token bajo
    /// <c>realm_access.roles</c>.
    /// </summary>
    private static readonly string RealmExportJson = $$"""
    {
      "realm": "{{RealmName}}",
      "enabled": true,
      "accessTokenLifespan": {{AccessTokenLifespanSeconds}},
      "sslRequired": "none",
      "clients": [
        {
          "clientId": "{{ClientId}}",
          "enabled": true,
          "protocol": "openid-connect",
          "publicClient": false,
          "secret": "{{ClientSecret}}",
          "serviceAccountsEnabled": true,
          "standardFlowEnabled": false,
          "directAccessGrantsEnabled": false,
          "clientAuthenticatorType": "client-secret",
          "fullScopeAllowed": true,
          "protocolMappers": [
            {
              "name": "audience-bitcode-api",
              "protocol": "openid-connect",
              "protocolMapper": "oidc-audience-mapper",
              "consentRequired": false,
              "config": {
                "included.client.audience": "{{ClientId}}",
                "id.token.claim": "false",
                "access.token.claim": "true"
              }
            },
            {
              "name": "realm-roles",
              "protocol": "openid-connect",
              "protocolMapper": "oidc-usermodel-realm-role-mapper",
              "consentRequired": false,
              "config": {
                "multivalued": "true",
                "userinfo.token.claim": "false",
                "id.token.claim": "false",
                "access.token.claim": "true",
                "claim.name": "realm_access.roles",
                "jsonType.label": "String"
              }
            }
          ]
        }
      ]
    }
    """;

    /// <summary>Realm export del segundo realm de prueba (ver <see cref="OtherRealmName"/>) -- mismo formato, issuer y claves de firma independientes.</summary>
    private static readonly string OtherRealmExportJson = $$"""
    {
      "realm": "{{OtherRealmName}}",
      "enabled": true,
      "accessTokenLifespan": {{AccessTokenLifespanSeconds}},
      "sslRequired": "none",
      "clients": [
        {
          "clientId": "{{OtherRealmClientId}}",
          "enabled": true,
          "protocol": "openid-connect",
          "publicClient": false,
          "secret": "{{ClientSecret}}",
          "serviceAccountsEnabled": true,
          "standardFlowEnabled": false,
          "directAccessGrantsEnabled": false,
          "clientAuthenticatorType": "client-secret",
          "protocolMappers": [
            {
              "name": "audience-bitcode-api-other",
              "protocol": "openid-connect",
              "protocolMapper": "oidc-audience-mapper",
              "consentRequired": false,
              "config": {
                "included.client.audience": "{{ClientId}}",
                "id.token.claim": "false",
                "access.token.claim": "true"
              }
            }
          ]
        }
      ]
    }
    """;
}
