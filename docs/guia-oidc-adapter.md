# Guía — Adapter OIDC/OAuth2 (F2-01), Authorization Code + PKCE (F2-02), BFF (F2-03), identidad de servicio (F2-04), validación de tokens (F2-05) y rotación/revocación (F2-06)

**Tarea:** F2-01 a F2-06 (Fase 2, Épica F2-A — Identidad y autenticación, **completa**) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Decisión de proveedor:** ver [ADR 0004](adr/0004-identidad-idp-oidc-oauth2.md) — Keycloak como IdP de referencia para desarrollo/CI, adapter escrito contra el estándar OIDC/OAuth2, sin acoplarse a particularidades de Keycloak.

## Qué resuelve esta tarea

`Shared.Infrastructure.Security.Oidc` agrega un segundo mecanismo de autenticación, alternativo al JWT propio existente (`Shared.Infrastructure.Security.Jwt`), que delega la emisión y firma de tokens a un IdP externo conforme a OpenID Connect:

- `OidcOptions` (sección de configuración `"Oidc"`): `Authority`, `Audience`, `ClientId` (reservado para F2-02/F2-04), `MetadataAddress` (override opcional), `RequireHttpsMetadata` y `ClockSkew` (F2-05, tolerancia de reloj para la validación de vigencia, default 30 segundos).
- `services.AddSharedOidcAuthentication(configuration)` (`OidcAuthenticationServiceCollectionExtensions`): registra `AddAuthentication().AddJwtBearer(...)` configurando únicamente `Authority`/`Audience`/`RequireHttpsMetadata` — el middleware de ASP.NET Core (`Microsoft.AspNetCore.Authentication.JwtBearer`) descubre automáticamente `{Authority}/.well-known/openid-configuration`, los endpoints y el JWKS de firma. Ningún código del framework lee un claim, header o endpoint propietario de Keycloak: **cambiar de proveedor es exclusivamente cambiar `Oidc:Authority`/`Oidc:Audience` en configuración**, el criterio de aceptación literal de F2-01.

```json
{
  "Oidc": {
    "Authority": "https://keycloak.local/realms/bitcode",
    "Audience": "bitcode-api"
  }
}
```

Apuntar a otro proveedor conforme a OIDC (por ejemplo, Microsoft Entra ID) es cambiar esos dos valores, sin tocar código:

```json
{
  "Oidc": {
    "Authority": "https://login.microsoftonline.com/<tenant-id>/v2.0",
    "Audience": "api://bitcode-api"
  }
}
```

## Qué NO resuelve F2-01 (alcance de tareas posteriores)

- **F2-02 (Authorization Code + PKCE):** ✅ implementado (ver sección siguiente).
- **F2-03 (BFF):** ✅ implementado (ver sección "F2-03 — BFF para aplicaciones Angular críticas").
- **F2-04 (Client Credentials/workload identity):** ✅ implementado (ver sección "F2-04 — Identidad de servicio").
- **F2-05 (Validación de tokens):** ✅ implementado (ver sección "F2-05 — Validación de tokens: clock skew explícito y casos negativos automatizados").
- **F2-06 (Rotación y revocación):** ✅ implementado (ver sección "F2-06 — Rotación de JWKS y revocación de sesiones/identidad de servicio").

## F2-02 — Authorization Code + PKCE para usuarios web

**Criterio de aceptación:** "Sin flujo implícito ni credenciales en SPA".

`BitCode.Framework.Shared.Infrastructure.Security.Oidc.AuthorizationCode` agrega el flujo Authorization Code + PKCE (RFC 6749 + RFC 7636) que consumen los proyectos con usuarios web interactivos, apoyado en el adapter de F2-01:

- `PkceGenerator` (estático, sin dependencias): `GenerateCodeVerifier()`, `CreateCodeChallenge(codeVerifier)` (método `S256` únicamente, verificado contra el vector de prueba oficial de RFC 7636 Apéndice B) y `GenerateCorrelationToken()` para `state`/`nonce`.
- `OidcAuthorizationCodeFlowOptions` (sección `"Oidc:AuthorizationCode"`): `RedirectUri` (obligatorio), `Scope` (`"openid profile email"` por defecto) y `CorrelationCookieLifetime` (10 minutos por defecto).
- `IOidcDiscoveryDocumentProvider`/`OidcDiscoveryDocumentProvider`: resuelve `authorization_endpoint`/`token_endpoint` por descubrimiento OIDC estándar (nunca hardcodeado — mismo principio que F2-01), cacheados 24hs en memoria de proceso (`OidcDiscoveryDocumentCache`).
- `IOidcAuthorizationRequestFactory`/`OidcAuthorizationRequestFactory`: construye la URL de autorización con `response_type=code` — **el único valor que este código puede producir, nunca `token`/`id_token`** — más `code_challenge`/`code_challenge_method=S256` y `state`/`nonce` de un solo uso.
- `IOidcAuthorizationCodeExchanger`/`OidcAuthorizationCodeExchanger`: intercambia el `code` por tokens contra el `token_endpoint` aportando `code_verifier` en vez de un `client_secret` — un cliente público protegido por PKCE nunca necesita guardar una credencial confidencial. Un `code` inválido/expirado y un `code_verifier` que no corresponde al `code_challenge` original (PKCE mismatch) llegan ambos como `invalid_grant` del IdP y se traducen al mismo `Result.Failure` (`ErrorType.Unauthorized`).
- `IOidcAuthorizationCodeStateProtector`/`OidcAuthorizationCodeStateProtector`: cifra/firma (`Microsoft.AspNetCore.DataProtection`) el `state`/`code_verifier`/`nonce`/`returnUrl` antes de guardarlos en la cookie de correlación.
- `services.AddSharedOidcAuthorizationCodeFlow(configuration)` (`OidcAuthorizationCodeServiceCollectionExtensions`, Shared.Infrastructure.Security): registra todo lo anterior, validando que `Oidc:Authority`/`Oidc:ClientId`/`Oidc:AuthorizationCode:RedirectUri` estén presentes. Las llamadas HTTP salientes (descubrimiento e intercambio de code) reutilizan la pipeline de resiliencia estándar del framework (F1-26, `AddResilientHttpClient`).
- `app.MapSharedOidcAuthorizationCodeLogin(onSignedIn, loginPath, callbackPath)` (`OidcAuthorizationCodeEndpointRouteBuilderExtensions`, **Shared.Infrastructure.Web** — no Security, para no forzar una `FrameworkReference` a ASP.NET Core en el proyecto de Security): mapea `GET /auth/login` (redirige al IdP, deja una cookie de correlación **HttpOnly + Secure + SameSite=Lax**, de un solo uso) y `GET /auth/callback` (valida `state`, descarta la cookie, intercambia el `code` y llama a `onSignedIn` con los tokens obtenidos).

```json
{
  "Oidc": {
    "Authority": "https://keycloak.local/realms/bitcode",
    "Audience": "bitcode-api",
    "ClientId": "bitcode-spa",
    "AuthorizationCode": {
      "RedirectUri": "https://api.bitcode.local/auth/callback"
    }
  }
}
```

```csharp
services.AddSharedOidcAuthorizationCodeFlow(configuration);
// ...
app.MapSharedOidcAuthorizationCodeLogin(onSignedIn: (httpContext, tokens, returnUrl, ct) =>
{
    // F2-02 termina acá a propósito: qué hacer con los tokens (sesión, cookie propia, redirect a la
    // SPA) es responsabilidad explícita del proyecto consumidor -- o de F2-03 (BFF), que es quien
    // reutilizará este mismo delegado para emitir una sesión server-side sin exponer ningún token al
    // navegador.
    return Task.FromResult(Results.Ok(new { tokens.AccessToken, returnUrl }));
});
```

### Por qué esto satisface "sin flujo implícito ni credenciales en SPA"

- `IOidcAuthorizationRequestFactory` solo puede emitir `response_type=code`: no existe ningún parámetro ni configuración que produzca `token`/`id_token` en la URL de autorización — el flujo implícito queda estructuralmente excluido, no solo desaconsejado.
- El `code_verifier` nunca sale del servidor: se genera y se usa server-side, y viaja al navegador únicamente dentro de una cookie **HttpOnly** (invisible para JavaScript, y por lo tanto para una SPA) mientras dura el flujo de login (10 minutos por defecto), protegida además con Data Protection.
- El intercambio de `code` por tokens (`IOidcAuthorizationCodeExchanger`) ocurre siempre server-side y sin `client_secret`: PKCE es exactamente el mecanismo que permite a un cliente público (una SPA, o el backend que actúa en su nombre) demostrar que es quien inició el flujo, sin guardar ninguna credencial confidencial en el navegador.
- La SPA nunca recibe el `code_verifier`, el `state` interno ni maneja el intercambio de tokens directamente: solo es redirigida por el backend (`/auth/login` → IdP → `/auth/callback`) y recibe el resultado final que decida `onSignedIn` — la base concreta que F2-03 (BFF) usará para no exponer ningún token al navegador.

### Qué queda explícitamente fuera de alcance de F2-02

- **F2-03 (BFF):** decidir e implementar qué hace `onSignedIn` con los tokens (sesión server-side, cookie propia de la aplicación, revocación) es tarea de F2-03. F2-02 entrega el mecanismo del flujo (login/callback/intercambio), no la gestión de sesión resultante.
- **F2-04/F2-05/F2-06:** sin cambios respecto de lo ya documentado para F2-01.

## Pruebas de F2-02

- `tests/Shared.Infrastructure.Security.Tests/Oidc/AuthorizationCode/PkceGeneratorTests.cs`: longitud/alfabeto de `code_verifier` (RFC 7636), `CreateCodeChallenge` contra el vector de prueba oficial de la RFC, unicidad de `state`/`nonce`.
- `tests/Shared.Infrastructure.Security.Tests/Oidc/AuthorizationCode/OidcAuthorizationRequestFactoryTests.cs`: `response_type=code` siempre, `code_challenge_method=S256`, ausencia de `client_secret`, `state`/`nonce` únicos por request.
- `tests/Shared.Infrastructure.Security.Tests/Oidc/AuthorizationCode/OidcAuthorizationCodeExchangerTests.cs`: intercambio exitoso, ausencia de `client_secret` en el body, **code inválido** y **PKCE mismatch** (ambos vía `invalid_grant` del IdP simulado) mapeados a `Result.Failure`.
- `tests/Shared.Infrastructure.Security.Tests/Oidc/AuthorizationCode/OidcDiscoveryDocumentProviderTests.cs`: resolución y cacheo del documento de metadata, `MetadataAddress` override.
- `tests/Shared.Infrastructure.Security.Tests/Oidc/AuthorizationCode/OidcAuthorizationCodeStateProtectorTests.cs`: round-trip, cookie tamperada, valor de otro propósito de protección.
- `tests/Shared.Infrastructure.Web.Tests/Security/Oidc/OidcAuthorizationCodeEndpointRouteBuilderExtensionsTests.cs`: `/auth/login` redirige con `response_type=code` y cookie HttpOnly+Secure; `/auth/callback` con **state inválido**, cookie de correlación ausente/tamperada, error del IdP, **code inválido** y **PKCE mismatch** reportados por el exchanger, y el camino exitoso invocando `onSignedIn`.

## F2-03 — BFF para aplicaciones Angular críticas

**Criterio de aceptación:** "Tokens no quedan expuestos al navegador".

F2-03 reutiliza literalmente el mecanismo de F2-02 (`MapSharedOidcAuthorizationCodeLogin`, el delegado `onSignedIn`) para que el resultado del intercambio de `code` nunca llegue al navegador: en su lugar, crea una sesión de cookie propia del BFF, guarda los tokens server-side, y expone un proxy (YARP) que los adjunta a las llamadas hacia las APIs protegidas.

### Piezas

- `BitCode.Framework.Shared.Infrastructure.Security.Oidc.Bff` (Shared.Infrastructure.Security — protocolo/almacenamiento, sin dependencia de ASP.NET Core más allá de las abstracciones de caching):
  - `BffSession`: lo que se guarda server-side por sesión (access/refresh/id token, vigencia del access token, claims mínimos del id_token).
  - `IBffSessionStore`/`DistributedCacheBffSessionStore`: persiste `BffSession` sobre `IDistributedCache`, cifrado con Data Protection (defensa en profundidad si la caché es compartida, p. ej. Redis). Identificador de sesión de 256 bits de entropía — el único dato que llega al navegador.
  - `BffSessionOptions` (sección `"Bff:Session"`): `CookieName` (`"bc-bff-session"` por defecto) e `IdleTimeout` (expiración deslizante, 30 minutos por defecto).
  - `services.AddSharedBffSessionStore(configuration)`: registra el store. Si el proyecto no llamó antes a `AddSharedCaching` (Shared.Infrastructure.Caching) con Redis configurado, agrega un fallback en memoria de proceso (`AddDistributedMemoryCache`, válido solo para desarrollo/una instancia) — **reutiliza la misma abstracción `IDistributedCache` del resto del framework, no introduce un mecanismo de sesión nuevo**.
- `BitCode.Framework.Shared.Infrastructure.Web.Security.Bff` (Shared.Infrastructure.Web — wiring HTTP):
  - `BffAuthenticationDefaults.Scheme` (`"BitCode.Bff"`): esquema de autenticación por cookie del BFF, distinto de `"Bearer"` (F2-01).
  - `BffTicketStore` (`ITicketStore`, interno): adapta `IBffSessionStore` al mecanismo estándar de "server-side session" de `Microsoft.AspNetCore.Authentication.Cookies` (`CookieAuthenticationOptions.SessionStore`) — la pieza que garantiza que la cookie del navegador contenga únicamente un identificador de sesión opaco, nunca el `AuthenticationTicket` cifrado con los tokens dentro (que es lo que pasaría sin un `SessionStore` custom).
  - `services.AddSharedBffCookieAuthentication(configuration)`: registra `IBffSessionStore` y el esquema de cookie (`HttpOnly` + `Secure` + `SameSite=Strict`, expiración deslizante) respaldado por `BffTicketStore`. Las llamadas AJAX sin sesión válida reciben `401`/`403` (no un redirect HTML) — la SPA decide cuándo navegar a `/auth/login`.
  - `BffOidcSignInHandler.HandleAsync`: implementación de referencia del delegado `onSignedIn` de F2-02. Decodifica (sin validar firma — llegó server-side, directo del `token_endpoint`, por PKCE) los claims del id_token, arma el `ClaimsPrincipal` y los tokens vía `AuthenticationProperties.StoreTokens`, llama `HttpContext.SignInAsync` y redirige a un `returnUrl` **validado como ruta local** (mismo criterio que `LocalRedirectResult`, para no introducir un open redirect). Falla (`Error.Failure`, sin crear sesión) si el IdP no devolvió `id_token`.
  - `BffLogoutEndpointRouteBuilderExtensions.MapSharedBffLogout` (`POST /auth/logout`, requiere sesión): revoca la sesión server-side (`SignOutAsync`, que internamente llama a `IBffSessionStore.RemoveAsync`) y, si el IdP publica `end_session_endpoint` (RP-Initiated Logout, extensión estándar del documento de descubrimiento OIDC — ver `OidcDiscoveryDocument.EndSessionEndpoint`), devuelve esa URL en el body (`{ "idpEndSessionUri": "..." }`) para que la SPA decida navegar allí y cerrar también la sesión SSO. Es una respuesta JSON, no un `302` server-side, porque este endpoint está pensado para invocarse con `fetch()`.
  - `BffAccessTokenRequestTransform` (`Yarp.ReverseProxy.Transforms.RequestTransform`): adjunta el access token de la sesión (`HttpContext.GetTokenAsync(BffAuthenticationDefaults.Scheme, "access_token")`) como header `Authorization: Bearer` de la request reenviada — sobrescribiendo cualquier `Authorization` que el navegador hubiera mandado (nunca debería tener uno, pero es defensa en profundidad).
  - `services.AddSharedBffProxy(configuration)` + `app.MapSharedBffProxy()`: registran YARP (`AddReverseProxy().LoadFromConfig(configuration.GetSection("ReverseProxy"))`, topología estándar de rutas/clusters de YARP, no impuesta por el framework) con el transform anterior aplicado a todas las rutas, y exigen la sesión de cookie del BFF (`RequireAuthorization`) antes de reenviar cualquier request.

```json
{
  "Bff": {
    "Session": {
      "CookieName": "bc-bff-session",
      "IdleTimeout": "00:30:00"
    }
  },
  "ReverseProxy": {
    "Routes": {
      "api-route": { "ClusterId": "api-cluster", "Match": { "Path": "/bff/api/{**catch-all}" } }
    },
    "Clusters": {
      "api-cluster": { "Destinations": { "d1": { "Address": "https://api.bitcode.local/" } } }
    }
  }
}
```

```csharp
services.AddSharedOidcAuthorizationCodeFlow(configuration); // F2-02
services.AddSharedBffCookieAuthentication(configuration);   // F2-03: sesión de cookie del BFF
services.AddSharedBffProxy(configuration);                  // F2-03: proxy YARP hacia las APIs
// ...
app.MapSharedOidcAuthorizationCodeLogin(onSignedIn: BffOidcSignInHandler.HandleAsync);
app.MapSharedBffLogout();
app.MapSharedBffProxy();
```

### Por qué esto satisface "tokens no quedan expuestos al navegador"

- La única cookie que recibe el navegador (`bc-bff-session`) es **HttpOnly + Secure + SameSite=Strict** y contiene un identificador de sesión opaco de 256 bits de entropía — nunca el access/refresh/id token, ni siquiera cifrados: `BffTicketStore` se asegura de que el `AuthenticationTicket` (que sí contiene los tokens) jamás se serialice dentro de la cookie.
- Los tokens viven exclusivamente en `IBffSessionStore` (server-side), cifrados con Data Protection antes de escribirse en la caché distribuida — ni siquiera un operador con acceso de lectura a Redis/Valkey los ve en texto plano.
- El proxy del BFF (`BffAccessTokenRequestTransform`) es el único punto donde el access token sale del servidor, y lo hace hacia la API protegida (server-to-server), nunca de vuelta hacia el navegador.
- El logout revoca la sesión server-side de inmediato (`IBffSessionStore.RemoveAsync`): la misma cookie que el navegador conserva deja de autenticar cualquier cosa a partir de ese momento.

### Dónde vive la sesión server-side

`IBffSessionStore` se apoya en `IDistributedCache` — la misma abstracción que ya usa `Shared.Infrastructure.Caching` (F1-16/F1-25). En un proyecto que llama a `AddSharedCaching` con `Caching:RedisConnectionString` configurado, la sesión del BFF automáticamente queda compartida entre instancias sobre ese mismo Redis/Valkey, sin ningún cambio de código. Sin Redis configurado, `AddSharedBffSessionStore` agrega un fallback en memoria de proceso — válido para desarrollo o una única instancia, pero **no sobrevive un balanceo entre instancias ni un reinicio del proceso** (una sesión activa se pierde), algo aceptable únicamente fuera de producción.

### Qué queda explícitamente fuera de alcance de F2-03

- **Renovación silenciosa con el refresh token:** `BffSession.RefreshToken` se persiste server-side, pero ni F2-03 ni F2-06 lo usan todavía para renovar un access token vencido sin volver a pasar por `/auth/login` -- sigue fuera de alcance, ver "Qué queda explícitamente fuera de alcance de F2-06".
- **Habilitación productiva del gateway/proxy YARP:** el ADR 0007 clasifica eso como "habilitación de tráfico productivo" (Plan Maestro, sección 13), que requiere aprobación humana explícita y es un punto de decisión separado de la construcción del proxy en sí (que sí se entrega en esta tarea).
- **F2-04 (Client Credentials/workload identity):** ✅ implementado (ver sección "F2-04 — Identidad de servicio"), independiente de la sesión de usuario del BFF -- el BFF sigue sin identidad de servicio propia en esta tarea, F2-04 aplica a cualquier workload backend-a-backend.
- **F2-05 (validación completa de tokens):** sin cambios respecto de lo ya documentado.
- **F2-06 (revocación de sesiones):** ✅ implementado (ver sección "F2-06 — Rotación de JWKS y revocación de sesiones/identidad de servicio") -- revocación de una sesión puntual (`IBffSessionStore.RemoveAsync`, ya existía desde F2-03) y de todas las sesiones de un sujeto (`RevokeAllForSubjectAsync`, nuevo en F2-06).

## Pruebas de F2-03

- `tests/Shared.Infrastructure.Security.Tests/Oidc/Bff/DistributedCacheBffSessionStoreTests.cs`: round-trip de la sesión, identificadores opacos únicos, revocación (`RemoveAsync`), renovación en el mismo identificador (`RenewAsync`), y dos casos de manipulación (entrada de caché tamperada, keyring de Data Protection distinto) que deben resolver a "sesión ausente" en vez de lanzar una excepción.
- `tests/Shared.Infrastructure.Web.Tests/Security/Bff/BffCookieSessionEndpointsTests.cs`: extremo a extremo contra un `TestServer` real — el callback de login nunca incluye el access token en ningún header o body de la respuesta; la cookie de sesión es HttpOnly+Secure+SameSite=Strict con un valor que tampoco lo contiene; un endpoint protegido puede recuperar el token de la sesión (representando lo que hace el proxy); sin cookie, `401`; logout revoca la sesión (la misma cookie deja de autenticar); y un id_token faltante nunca llega a crear una sesión.
- `tests/Shared.Infrastructure.Web.Tests/Security/Bff/BffAccessTokenRequestTransformTests.cs`: el transform de YARP adjunta el access token de la sesión como `Authorization: Bearer`, sobrescribe cualquier `Authorization` que el navegador hubiera mandado, y una request sin sesión nunca llega a ejecutarlo.

## F2-04 — Identidad de servicio (Client Credentials, workload identity)

**Criterio de aceptación:** "Cada workload tiene identidad propia".

`BitCode.Framework.Shared.Infrastructure.Security.Oidc.ServiceIdentity` agrega OAuth2 Client Credentials (RFC 6749 sección 4.4) para comunicación servicio-a-servicio: cada workload obtiene su propio token contra el `token_endpoint` del IdP usando sus propias credenciales de cliente (`client_id`/`client_secret`), sin usuario humano ni redirección involucrados -- a diferencia de F2-02/F2-03, que son mecanismos para un usuario final. Una identidad de servicio (F2-04) y la identidad OIDC que este mismo proceso valida como servidor (F2-01, `OidcOptions`) son conceptos independientes y pueden coexistir: un servicio puede validar tokens de usuario entrantes y, al mismo tiempo, presentar su propia identidad al llamar a otro servicio.

- `ServiceIdentityOptions` (sección `"Oidc:ServiceIdentity"`): `Authority` y `ClientId` obligatorios; uno de `ClientSecret` o `CertificateThumbprint` obligatorio; `Scope` (opcional), `MetadataAddress` (override opcional) y `RequireHttpsMetadata` (`true` por defecto).
- `ServiceAccessToken`: token de acceso propio (`AccessToken`, `TokenType`, `ExpiresAtUtc`).
- `IServiceTokenProvider`/`ServiceTokenProvider`: obtiene el token contra el `token_endpoint` resuelto por descubrimiento OIDC estándar (mismo principio que F2-01/F2-02 -- nunca un endpoint hardcodeado), reutilizando la pipeline de resiliencia estándar del framework (F1-26, `AddResilientHttpClient`) tanto para la resolución de metadata como para la solicitud de token.
- `ServiceTokenCache` (singleton): cachea el token vigente y el `token_endpoint` resuelto. Un token se reutiliza mientras le queden más de 60 segundos de vigencia (margen de renovación fijo, pensado para que ninguna llamada saliente use un token que expira a mitad de la petición); por debajo de ese margen se solicita uno nuevo automáticamente en la siguiente llamada. Un fallo al pedir un token nunca se cachea -- la siguiente llamada vuelve a intentar, en vez de recordar un error transitorio del IdP.
- `ServiceIdentityAuthenticationHandler` (`DelegatingHandler`): adjunta el token de servicio como header `Authorization` a cada request saliente de un `HttpClient` tipado. Si no se pudo obtener un token, lanza `InvalidOperationException` en vez de reenviar la request en forma anónima hacia una API protegida.
- `services.AddSharedServiceIdentity(configuration)` (`ServiceIdentityServiceCollectionExtensions`): registra `IServiceTokenProvider`/`ServiceTokenCache`/`ServiceIdentityAuthenticationHandler`, validando que `Oidc:ServiceIdentity:Authority`/`ClientId` y uno de `ClientSecret`/`CertificateThumbprint` estén presentes.
- `builder.AddServiceIdentityAuthentication()` (extensión de `IHttpClientBuilder`): agrega el handler anterior a un cliente HTTP tipado ya registrado -- típicamente uno creado con `AddResilientHttpClient<TClient>` (F1-26), reutilizando esa misma pipeline en vez de declarar un `HttpClient` propio.

```json
{
  "Oidc": {
    "ServiceIdentity": {
      "Authority": "https://keycloak.local/realms/bitcode",
      "ClientId": "bitcode-workload-productos",
      "ClientSecret": "<secreto gestionado por el proveedor de secretos, F2-12 -- nunca en el repositorio>",
      "Scope": "api.pedidos.write"
    }
  }
}
```

```csharp
// Program.cs del workload que llama a otra API del framework en su propio nombre (p. ej. el servicio
// de Productos llamando al servicio de Pedidos para reservar stock).
services.AddSharedServiceIdentity(configuration);

services.AddResilientHttpClient<IPedidosApiClient>(configuration)   // F1-26
    .AddServiceIdentityAuthentication();                            // F2-04: adjunta la identidad propia

// ...

public interface IPedidosApiClient
{
    Task<Result> ReservarStockAsync(Guid pedidoId, CancellationToken cancellationToken);
}

public sealed class PedidosApiClient(HttpClient httpClient) : IPedidosApiClient
{
    // httpClient ya trae el header "Authorization: Bearer <token de servicio>" en cada request --
    // este código nunca maneja el token ni sabe cómo se obtuvo.
    public async Task<Result> ReservarStockAsync(Guid pedidoId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsync($"/api/v1/pedidos/{pedidoId}/reservar-stock", null, cancellationToken);
        return response.IsSuccessStatusCode
            ? Result.Success()
            : Result.Failure(Error.Failure("Pedidos.ReservarStock.Failed", $"El servicio de Pedidos respondió {(int)response.StatusCode}."));
    }
}
```

### Por qué esto satisface "cada workload tiene identidad propia"

- `ServiceIdentityOptions.ClientId`/`ClientSecret` son un cliente confidencial distinto por workload, registrado en el IdP exclusivamente para Client Credentials -- no hay una identidad compartida entre servicios ni una reutilización del `ClientId` público de F2-02.
- El token se pide y se cachea por proceso (`ServiceTokenCache` singleton): cada instancia de cada workload obtiene y renueva su propio token de forma independiente.
- El IdP es la única fuente de verdad para validar esa identidad de servicio del lado receptor -- el mismo middleware de F2-01/F2-05 que valida un token de usuario valida igual de bien un token de Client Credentials (ambos son JWT firmados por el mismo IdP, con `sub` distinto: el `client_id` del workload en vez del `sub` de un usuario).

### Qué queda explícitamente fuera de alcance de F2-04

- **Autenticación de cliente por certificado (mTLS, RFC 8705, o `private_key_jwt`, RFC 7523):** `ServiceIdentityOptions.CertificateThumbprint` es un campo de contrato preparado para esa evolución ("workload identity federada", Plan Maestro sección 2), pero el flujo concreto todavía no está implementado -- configurarlo sin `ClientSecret` falla explícitamente (`ServiceIdentity.CertificateAuthenticationNotSupported`) en vez de intentar algo no soportado en silencio. Implementarlo es una extensión futura, no bloqueada por nada de esta tarea.
- **F2-05 (validación completa de tokens):** el token de servicio recibido por la API destino se valida con exactamente el mismo middleware/política que un token de usuario (F2-01/F2-05) -- F2-04 no agrega ninguna validación adicional del lado receptor.
- **F2-06 (rotación y revocación):** revocar la identidad de un workload comprometido (rotar su `client_secret` en el IdP) sigue siendo una operación del lado del IdP -- F2-04 no agrega un mecanismo propio de revocación push. F2-06 sí confirma y prueba explícitamente cuál es la política resultante: ver sección "F2-06 — Rotación de JWKS y revocación de sesiones/identidad de servicio", "Revocación de identidad de servicio".

## Pruebas de F2-04

- `tests/Shared.Infrastructure.Security.Tests/Oidc/ServiceIdentity/ServiceTokenProviderTests.cs`: obtención exitosa de token, ausencia de scope cuando no está configurado y presencia cuando sí, **reutilización del token cacheado** mientras es válido (sin llamada HTTP adicional), **renovación automática** cuando el token está dentro del margen de expiración, **fallo de credenciales** (`invalid_client` del IdP simulado) mapeado a `Result.Failure` sin cachearse, ausencia de `ClientSecret`/`CertificateThumbprint` y `CertificateThumbprint` configurado (todavía no soportado) fallando sin ninguna llamada HTTP.
- `tests/Shared.Infrastructure.Security.Tests/Oidc/ServiceIdentity/ServiceIdentityServiceCollectionExtensionsTests.cs`: validación de configuración obligatoria, registro de `IServiceTokenProvider` por DI, y que `AddServiceIdentityAuthentication` agrega el handler a la pipeline de un cliente HTTP tipado.
- `tests/Shared.Infrastructure.Security.Tests/Oidc/ServiceIdentity/ServiceIdentityAuthenticationHandlerTests.cs`: el handler adjunta `Authorization: <TokenType> <AccessToken>` a la request saliente, y nunca reenvía la request si no se pudo obtener el token de servicio.

## F2-05 — Validación de tokens: clock skew explícito y casos negativos automatizados

F2-01 ya dejaba activas `ValidateIssuer`/`ValidateAudience`/`ValidateLifetime`/`ValidateIssuerSigningKey` en `TokenValidationParameters`, pero como comportamiento implícito del middleware, sin una tolerancia de reloj explícita ni pruebas de integración contra un IdP real que demostraran que cada validación efectivamente rechaza lo que dice rechazar. F2-05 cierra esa brecha:

- **`OidcOptions.ClockSkew`** (`TimeSpan`, default 30 segundos): tolerancia de reloj aplicada a la validación de vigencia (`exp`/`nbf`). El framework fija 30 segundos explícitamente en vez de heredar en silencio el default de **5 minutos** de `TokenValidationParameters` — para Zero Trust (Plan Maestro sección 1), una ventana de gracia de varios minutos después de que un token expira es una superficie de ataque innecesaria. Ampliarlo solo se justifica si se observan rechazos legítimos por desfase de reloj entre hosts (síntoma de un problema de NTP, no algo que deba resolverse subiendo el default).
- El comentario en código de `OidcAuthenticationServiceCollectionExtensions` documenta explícitamente que issuer/audience/firma/vigencia son un **contrato**, no un efecto colateral de no haber configurado nada distinto.

### Pruebas de F2-05

`tests/Shared.Infrastructure.Security.Tests/Integration/OidcTokenValidationIntegrationTests.cs` ejercita `AddSharedOidcAuthentication` de punta a punta contra un **Keycloak real** (`KeycloakContainerFixture`, Testcontainers, imagen `quay.io/keycloak/keycloak`) sirviendo tokens reales a un `TestServer` con un endpoint mínimo protegido por `RequireAuthorization()`:

- **Token válido** (control positivo): un access token real de Client Credentials contra un cliente confidencial del realm de prueba es aceptado (200).
- **Token expirado:** el realm de prueba fija `accessTokenLifespan` en pocos segundos; esperar más que eso produce 401 (`ValidateLifetime`).
- **Token con firma alterada:** se corrompe el tercer segmento (firma) de un JWT real y válido; sigue siendo 401 (`ValidateIssuerSigningKey`) aunque header y payload no se tocaron.
- **Issuer incorrecto:** el token es real y su firma es válida — se aísla específicamente la comprobación de issuer fijando un `ValidIssuer` distinto (vía el parámetro `configureJwtBearer` de `AddSharedOidcAuthentication`) para no confundir este caso con un fallo de firma o de JWKS.
- **Audience incorrecta:** mismo principio, aislando `ValidAudience`.
- **Clock skew:** un token vencido hace pocos segundos es aceptado cuando `ClockSkew` cubre ese margen, y rechazado cuando `ClockSkew` es cero — demuestra que el valor de `OidcOptions.ClockSkew` realmente gobierna el comportamiento, no solo que existe como propiedad.

`tests/Shared.Infrastructure.Security.Tests/OidcAuthenticationServiceCollectionExtensionsTests.cs` agrega, sin Keycloak (pruebas unitarias rápidas): `ClockSkew` por defecto es 30 segundos (no el default de 5 minutos del framework) y se puede sobrescribir desde configuración (`Oidc:ClockSkew`).

`KeycloakContainerFixture` (`src/Shared.Testing/KeycloakContainerFixture.cs`) importa al arrancar un realm mínimo (`bitcode-test`) con un cliente confidencial (`bitcode-api`, Service Accounts habilitado, mapper de audience fijo) — sigue el mismo patrón que `SqlServerContainerFixture`/`RedisContainerFixture` (un contenedor descartable por clase de test, credenciales fijas que solo existen dentro de ese contenedor efímero, nunca un secreto real).

### Limitación conocida de este entorno de ejecución

Estas pruebas requieren Docker (Testcontainers) y **se validaron manualmente contra un Keycloak real** en la máquina de desarrollo donde se implementó F2-05 (Docker Desktop disponible). Si el entorno de CI/ejecución no tiene Docker disponible, estas pruebas fallan al no poder arrancar el contenedor — no hay una alternativa "simulada" en este repositorio para F2-05 porque validar issuer/audience/firma/vigencia contra un IdP real (no un JWT autogenerado con una clave RSA propia) es precisamente lo que el criterio de aceptación de F2-05 pide para no repetir el mismo defecto de F2-01 (validar solo la forma del contrato, no el comportamiento real del middleware contra un servidor de tokens).

## F2-06 — Rotación de JWKS y revocación de sesiones/identidad de servicio

**Criterio de aceptación:** "Rotación sin downtime". Última tarea de la Épica F2-A -- cierra el ciclo de vida completo de un token/sesión: emisión (F2-01/F2-02/F2-04), validación (F2-05) y ahora rotación de claves y revocación.

F2-06 no introduce un mecanismo de rotación propio: verifica, deja explícito y prueba contra un IdP real que los tres mecanismos que ya existían (el refresco automático de JWKS del middleware estándar, `IBffSessionStore.RemoveAsync` de F2-03, y el "fail-closed" natural de `ServiceTokenCache` de F2-04) efectivamente cumplen la garantía que el criterio de aceptación pide -- y agrega la única pieza que realmente faltaba: revocar TODAS las sesiones de un sujeto de una sola vez.

### Rotación de JWKS sin downtime

- **`OidcOptions.RefreshOnIssuerKeyNotFound`** (`bool`, default `true`): si un token llega firmado con un `kid` que el JWKS cacheado no conoce, el middleware de JwtBearer refresca la metadata OIDC (issuer + JWKS) y reintenta la validación antes de rechazar el token -- **este es el mecanismo real detrás de "rotación sin downtime"**, no un intervalo de refresco periódico. Ya era el comportamiento por defecto del middleware subyacente; F2-06 lo deja explícito y contractual en `OidcOptions` para que no dependa de un default implícito de una versión futura de `Microsoft.AspNetCore.Authentication.JwtBearer`.
- **`OidcOptions.JwksMinimumRefreshInterval`** (`TimeSpan?`, sin configurar usa el default de `Microsoft.IdentityModel` de 5 minutos): acota, como anti-DoS, cada cuánto como máximo un `kid` desconocido puede disparar un refresco real contra el IdP -- en la práctica, el límite superior de la "ventana de downtime" real de una rotación: un token firmado con la clave recién rotada puede tardar hasta este intervalo en validarse la primera vez. El piso técnico de `Microsoft.IdentityModel` es 1 segundo (un valor menor lanza `ArgumentOutOfRangeException` al arrancar) -- solo se justifica bajarlo en pruebas automatizadas, nunca en producción (un valor demasiado bajo abre una vía de DoS contra el JWKS del IdP).
- **`OidcOptions.JwksAutomaticRefreshInterval`** (`TimeSpan?`, sin configurar usa el default de 12 horas, con un piso técnico de 5 minutos): intervalo de refresco periódico en segundo plano, independiente de si aparece o no un `kid` desconocido -- deliberadamente NO es el mecanismo de "rotación sin downtime" (ese es `RefreshOnIssuerKeyNotFound`); solo tiene sentido bajarlo si se quiere detectar antes una rotación aunque todavía no haya llegado ningún token firmado con la clave nueva.

```json
{
  "Oidc": {
    "Authority": "https://keycloak.local/realms/bitcode",
    "Audience": "bitcode-api"
  }
}
```

No hace falta configurar nada de lo anterior para tener "rotación sin downtime": es el comportamiento por defecto. `JwksMinimumRefreshInterval`/`JwksAutomaticRefreshInterval` existen para los casos donde un proyecto necesite afinar esos intervalos (o, como en la prueba de integración de F2-06, acortarlos para no depender de esperas reales de varios minutos).

### Revocación de sesiones del BFF

- **`IBffSessionStore.RemoveAsync(sessionId)`** (existía desde F2-03): revoca una sesión puntual -- ya cubría tanto el logout iniciado por el propio usuario (`MapSharedBffLogout`) como una revocación puntual decidida por otra parte del sistema (p. ej. detección de abuso sobre un `sessionId` conocido).
- **`IBffSessionStore.RevokeAllForSubjectAsync(subject)`** (nuevo en F2-06): revoca de una sola vez TODAS las sesiones activas de un sujeto (claim `"sub"` del id_token) -- el mecanismo detrás de "cerrar sesión en todos los dispositivos" y de una revocación administrativa (baja de usuario, sospecha de credenciales comprometidas) sin conocer de antemano los `sessionId` individuales. `DistributedCacheBffSessionStore` lo implementa con un índice `sujeto -> [sessionId...]` mantenido en el mismo `IDistributedCache`, cifrado con Data Protection igual que la sesión misma -- **deliberadamente best-effort, no transaccional** (`IDistributedCache` no ofrece operaciones atómicas de conjunto): en el peor caso (dos logins concurrentes del mismo sujeto en la misma ventana de milisegundos, en instancias distintas) una entrada puede perderse del índice, pero eso nunca compromete la revocación por `sessionId` individual -- la fuente de verdad de "¿esta sesión sigue vigente?" siempre es `GetAsync` contra la sesión misma, nunca el índice.
- **`MapSharedBffLogoutAllDevices`** (`POST /auth/logout-all`, requiere sesión, `Shared.Infrastructure.Web`): endpoint de autoservicio que revoca todas las sesiones del sujeto autenticado que hace la llamada -- el sujeto se toma siempre del `ClaimsPrincipal` de la sesión, nunca de un parámetro de la request, así que un llamador solo puede revocar sus propias sesiones a través de este endpoint. Una revocación administrativa de las sesiones de OTRO sujeto es un caso de uso distinto, que debe exponerse detrás de una autorización explícita (RBAC/ABAC, Épica F2-B) invocando directamente `IBffSessionStore.RevokeAllForSubjectAsync`, no este endpoint.

```csharp
app.MapSharedBffLogout();            // F2-03: cierra únicamente la sesión que hizo la llamada
app.MapSharedBffLogoutAllDevices();  // F2-06: cierra TODAS las sesiones del mismo sujeto
```

### Revocación de identidad de servicio

OAuth2 Client Credentials (F2-04) no tiene un canal de revocación "push": el IdP no puede avisarle a un workload que su `ClientSecret` fue revocado. La política resultante, confirmada y probada explícitamente en F2-06:

1. Mientras un token ya obtenido siga vigente en `ServiceTokenCache` (fuera del margen de renovación de 60 segundos), se sigue sirviendo sin volver a contactar al IdP -- esto es intencional (evitar una llamada HTTP por request es la razón de ser del cache), no un agujero de seguridad: el token en sí sigue siendo validado por su propia vigencia (`exp`) en cada API que lo reciba (F2-05).
2. En cuanto el cache necesita renovar el token (vencido, o dentro del margen de renovación), vuelve a autenticarse contra el IdP con el `ClientSecret` configurado -- si ya fue revocado/rotado, el IdP lo rechaza (`invalid_client`) exactamente igual que a un cliente nuevo con esa misma credencial. `ServiceTokenCache` nunca cachea ese fallo (ver `GetAccessTokenAsync_FailedAttempt_IsNotCached_NextCallRetries`), así que ninguna llamada posterior vuelve a obtener un token utilizable hasta que se configure un `ClientSecret` válido.
3. **Rotación planeada sin downtime:** para rotar el `ClientSecret` de un workload sin una ventana de falla, el operador debe (a) crear/activar la credencial nueva en el IdP manteniendo la vieja activa en paralelo, (b) desplegar la configuración con el secreto nuevo en el workload, y solo después (c) revocar la credencial vieja en el IdP -- si se revoca la vieja antes de que el workload tenga la nueva desplegada, el próximo refresh de `ServiceTokenCache` (dentro de, como máximo, el margen de renovación de 60 segundos) falla con `invalid_client` hasta que el despliegue con el secreto nuevo complete.

### Política operativa de rotación de `client_secret` (BFF y servicio)

No existe todavía un mecanismo automatizado de rotación de `client_secret` en este repositorio (eso requeriría integrar con el proveedor de secretos de la Épica F2-C, todavía no construida) -- la política operativa mientras tanto:

- **Frecuencia recomendada:** rotar todo `client_secret` (BFF y cada identidad de servicio de F2-04) al menos cada 90 días, o inmediatamente ante sospecha de compromiso -- alineado con la práctica estándar de rotación de credenciales de aplicación (no específico de OIDC).
- **Procedimiento sin downtime (BFF, Authorization Code):** el `ClientId` del BFF ante el IdP normalmente no requiere `client_secret` si se registra como cliente público con PKCE (F2-02) -- si el proyecto lo registró como confidencial igual, aplica el mismo procedimiento de "credencial nueva en paralelo, desplegar, recién después revocar la vieja" que la identidad de servicio (ver arriba).
- **Procedimiento sin downtime (identidad de servicio, F2-04):** ver el punto 3 de la sección anterior -- es el mismo principio en los tres casos: nunca revocar la credencial vieja antes de que la nueva esté desplegada y en uso.
- **Coordinación entre BFF/service identity y Keycloak:** ambos casos dependen de que el IdP permita más de una credencial activa simultáneamente por cliente durante la ventana de rotación (Keycloak lo permite regenerando el secreto solo cuando se decide, no automáticamente) -- la responsabilidad de no revocar antes de tiempo es enteramente operativa, no hay ninguna protección de código que la haga cumplir.
- **Camino futuro:** cuando la Épica F2-C (secretos) esté construida, la rotación debería automatizarse (secreto gestionado con vigencia corta, renovado por el proveedor de secretos sin intervención manual) -- hasta entonces, este procedimiento manual es la política vigente.

### Qué queda explícitamente fuera de alcance de F2-06

- **Renovación silenciosa de la sesión del BFF con el refresh token:** `BffSession.RefreshToken` se sigue persistiendo server-side sin usarse todavía -- una sesión BFF vencida sigue requiriendo pasar de nuevo por `/auth/login`, no una renovación transparente. Queda para una tarea futura si se decide priorizarla.
- **Revocación administrativa de sesiones de OTRO sujeto vía un endpoint HTTP:** el primitivo (`IBffSessionStore.RevokeAllForSubjectAsync`) ya existe y es invocable desde código de servicio, pero exponerlo como endpoint HTTP para un administrador requiere autorización explícita (RBAC/ABAC, Épica F2-B, todavía no construida) -- F2-06 entrega el mecanismo, no la política de acceso a él.
- **Rotación automatizada de `client_secret`:** ver "Camino futuro" arriba -- depende de la Épica F2-C (secretos).
- **Revocación de tokens de acceso individuales antes de su expiración natural (token revocation endpoint, RFC 7009):** ni el BFF ni la identidad de servicio implementan una llamada al `revocation_endpoint` del IdP para invalidar un access/refresh token específico antes de que expire por sí solo -- la revocación de F2-06 opera al nivel de sesión (BFF) o de credencial (identidad de servicio), no al nivel de token individual. Evaluar si hace falta queda para una tarea futura si el threat model lo requiere.

### Pruebas de F2-06

- `tests/Shared.Infrastructure.Security.Tests/Integration/OidcTokenValidationIntegrationTests.cs` (`Token_FirmadoConClaveRotadaEnElIdp_EsAceptadoSinReiniciarLaAplicacion`): rota realmente la clave de firma activa de un realm de Keycloak vía la Admin REST API (`KeycloakContainerFixture.RotateSigningKeyAsync`, `POST /admin/realms/{realm}/components`, `providerId=rsa-generated` con prioridad más alta) y confirma que un token nuevo, firmado con la clave rotada, se valida correctamente en el mismo proceso/`TestServer` ya en ejecución -- sin reiniciar nada.
- `tests/Shared.Infrastructure.Security.Tests/Oidc/Bff/DistributedCacheBffSessionStoreTests.cs`: `RevokeAllForSubjectAsync` revoca todas las sesiones activas de un sujeto, no afecta las de otro sujeto, es idempotente sobre un sujeto sin sesiones, y el índice se mantiene consistente a través de `RemoveAsync`/`RenewAsync`.
- `tests/Shared.Infrastructure.Web.Tests/Security/Bff/BffCookieSessionEndpointsTests.cs`: `MapSharedBffLogoutAllDevices` revoca dos sesiones del mismo sujeto (dos "dispositivos") con una sola llamada, incluida la que hizo la llamada, y un intento sin sesión no revoca nada y devuelve `401`.
- `tests/Shared.Infrastructure.Security.Tests/Oidc/ServiceIdentity/ServiceTokenProviderTests.cs`: un token ya cacheado y vigente se sigue sirviendo sin contactar al IdP aunque el `ClientSecret` ya haya sido revocado (no hay revocación push), y la siguiente renovación real falla (`invalid_client`) en cuanto el cache decide que el token anterior ya no es utilizable.

## Coexistencia con el JWT propio existente

`Shared.Infrastructure.Security.SecurityServiceCollectionExtensions.AddSharedSecurity<TUser, TRole, TContext>` (JWT propio: `Microsoft.AspNetCore.Authentication.JwtBearer` con clave simétrica emitida localmente, `Jwt:SecretKey`/`Jwt:Issuer`/`Jwt:Audience`) **no se modifica ni se retira en esta tarea**. Ambos mecanismos registran el mismo esquema de autenticación (`"Bearer"`), por lo que son **mutuamente excluyentes dentro del mismo proyecto**: un proyecto llama a uno o al otro para la parte de autenticación, nunca a los dos.

- Un proyecto existente que ya usa `AddSharedSecurity` sigue funcionando exactamente igual — no hay ningún cambio de comportamiento ni de compatibilidad para él en esta tarea (`samples/Sample.Api` no fue tocado).
- Un proyecto nuevo, o uno que decida migrar a un IdP externo, llama a `AddSharedOidcAuthentication` en su lugar.
- `AddSharedSecurity` sigue siendo el único camino hoy para Identity/roles/permisos (`IdentityCore<TUser>`, `RoleManager<TRole>`, `IPermissionService`) — desacoplar esa parte de la emisión de JWT propio (para que un proyecto use Identity/permisos de BitCode junto con autenticación OIDC externa) queda fuera de alcance de F2-01 y no está resuelto todavía; se evaluará en una tarea posterior de la Épica F2-A/F2-B según haga falta.
- El JWT propio **no se marca `[Obsolete]` en esta tarea**: hacerlo dispararía advertencias de compilación en todos sus consumidores actuales (tests, samples) sin el período de gracia y el análisis de impacto que exige `docs/politica-versionado.md` (sección 3). La deprecación formal (marcar `[Obsolete]` con mensaje y alternativa, y definir la ventana de retiro) es una decisión a tomar explícitamente cuando F2-02/F2-03/F2-05 dejen el camino OIDC completo end-to-end, no antes.

## Pruebas

`tests/Shared.Infrastructure.Security.Tests/OidcAuthenticationServiceCollectionExtensionsTests.cs` cubre, sin dependencias externas (no requiere una instancia real de Keycloak — eso lo cubre `OidcTokenValidationIntegrationTests`, ver sección F2-05):

- El esquema `"Bearer"` queda registrado como default.
- `Authority`/`Audience`/`RequireHttpsMetadata`/`MetadataAddress`/`ClockSkew` se propagan desde `IConfiguration` a `JwtBearerOptions` sin ningún valor hardcodeado.
- Dos configuraciones distintas (`Authority` de Keycloak vs. de Entra ID) producen `JwtBearerOptions` distintos con el mismo código de registro — evidencia directa del criterio de aceptación "proveedor intercambiable por configuración".
- Falta la sección `"Oidc"`, o falta `Authority`/`Audience` dentro de ella, lanza `InvalidOperationException` (mismo patrón que `AddSharedSecurity` con la sección `"Jwt"`).
- `ClockSkew` por defecto es 30 segundos (no el default de 5 minutos de `TokenValidationParameters`), y admite override desde configuración.

## Referencias

- [ADR 0004](adr/0004-identidad-idp-oidc-oauth2.md) — decisión de IdP (Keycloak) y por qué el adapter debe seguir siendo agnóstico de proveedor.
- `docs/convenciones.md` — reglas duras del framework, aplicables sin excepción al código nuevo de esta tarea.
- `docs/politica-versionado.md` (sección 3) — política de deprecación que gobierna cuándo y cómo se marcará obsoleto el JWT propio.
- [ADR 0007](adr/0007-gateway-yarp.md) — decisión de YARP como tecnología de gateway/reverse proxy, base del proxy del BFF de F2-03.
- `src/Shared.Infrastructure.Security/Oidc/` — código del adapter (F2-01), del flujo Authorization Code + PKCE (F2-02, subcarpeta `AuthorizationCode/`), de la sesión server-side del BFF (F2-03, subcarpeta `Bff/`) y de la identidad de servicio (F2-04, subcarpeta `ServiceIdentity/`).
- `src/Shared.Infrastructure.Web/Security/Oidc/` — mapeo de endpoints HTTP de F2-02 (`MapSharedOidcAuthorizationCodeLogin`).
- `src/Shared.Infrastructure.Web/Security/Bff/` — cookie de sesión, `onSignedIn` de referencia, logout y proxy YARP de F2-03.
- `tests/Shared.Infrastructure.Security.Tests/OidcAuthenticationServiceCollectionExtensionsTests.cs` — pruebas de F2-01.
- `tests/Shared.Infrastructure.Security.Tests/Oidc/AuthorizationCode/` y `tests/Shared.Infrastructure.Web.Tests/Security/Oidc/` — pruebas de F2-02.
- `tests/Shared.Infrastructure.Security.Tests/Oidc/Bff/` y `tests/Shared.Infrastructure.Web.Tests/Security/Bff/` — pruebas de F2-03.
- `tests/Shared.Infrastructure.Security.Tests/Oidc/ServiceIdentity/` — pruebas de F2-04.
- `tests/Shared.Infrastructure.Security.Tests/Integration/OidcTokenValidationIntegrationTests.cs` y `Integration/KeycloakCollection.cs` — pruebas de F2-05 y de rotación de JWKS de F2-06 contra Keycloak real (Testcontainers).
- `src/Shared.Testing/KeycloakContainerFixture.cs` — fixture de Testcontainers para Keycloak, reutilizable por cualquier prueba de integración que necesite un IdP OIDC real; incluye `RotateSigningKeyAsync` (F2-06) para simular una rotación real de clave de firma vía la Admin REST API.
- `src/Shared.Infrastructure.Security/Oidc/OidcOptions.cs` — `RefreshOnIssuerKeyNotFound`/`JwksMinimumRefreshInterval`/`JwksAutomaticRefreshInterval` (F2-06).
- `src/Shared.Infrastructure.Security/Oidc/Bff/IBffSessionStore.cs` y `DistributedCacheBffSessionStore.cs` — `RevokeAllForSubjectAsync` (F2-06).
- `src/Shared.Infrastructure.Web/Security/Bff/BffLogoutEndpointRouteBuilderExtensions.cs` — `MapSharedBffLogoutAllDevices` (F2-06).
