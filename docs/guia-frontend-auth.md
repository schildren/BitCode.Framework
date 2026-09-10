# Guía — `@bitcode/auth` (F7-03, Fase 7 — Plataforma Angular empresarial)

> Tarea de origen: F7-03 (Autenticación) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
> Alcance: sesión, login/logout, guard de "hay sesión sí/no", claims tipados e interceptor HTTP del
> paquete `frontend/packages/auth/` (`@bitcode/auth`). Autorización consciente de roles/permisos
> (RBAC/ABAC) es F7-04, no esta tarea.

## 1. Contrato del backend que este paquete consume

`@bitcode/auth` es un cliente del mecanismo BFF/OIDC descrito en `docs/guia-oidc-adapter.md`
(F2-01 a F2-06, **completo y probado a nivel de librería .NET**): Authorization Code + PKCE, sesión de
cookie server-side (`bc-bff-session`, HttpOnly + Secure + SameSite=Strict) y proxy YARP que adjunta el
access token del lado del servidor. El navegador nunca ve un access/refresh/id token.

**Endpoints documentados y reutilizados tal cual:**

| Endpoint | Método | De dónde sale |
|---|---|---|
| `/auth/login` | `GET` (navegación completa, nunca `fetch`) | F2-02/F2-03, `MapSharedOidcAuthorizationCodeLogin` |
| `/auth/logout` | `POST` (JSON, pensado para `fetch`) | F2-03, `MapSharedBffLogout` |
| `/auth/logout-all` | `POST` (JSON) | F2-06, `MapSharedBffLogoutAllDevices` |

**Endpoint que este paquete PROPONE y que NO existe todavía en ningún host real de BitCode:**

| Endpoint | Método | Estado |
|---|---|---|
| `/bff/session` (configurable, `BitcodeAuthConfig.sessionEndpoint`) | `GET` → 200 con los claims de la sesión actual, o 401 si no hay sesión | **Pendiente real de backend**, ver sección 5 |

`docs/guia-oidc-adapter.md` no documenta ningún endpoint "quién soy" (tipo `GET /bff/user`, patrón común
en implementaciones BFF como Duende.BFF) -- ningún `Shared.Infrastructure.Web/Security/Bff/*` actual lo
expone. `@bitcode/auth` necesita uno para poder resolver "¿hay sesión activa?" sin decodificar nada del
lado cliente, así que este paquete define el contrato mínimo que espera (`GET`, 200 con JSON de claims o
401) y lo deja configurable por endpoint vía `BitcodeAuthConfig.sessionEndpoint` para no acoplarse a un
nombre fijo. Agregar el endpoint real al backend es un pendiente explícito de una tarea futura (ver
sección 5), no algo resuelto ni prometido como ya cerrado por esta tarea de Fase 7.

## 2. Qué implementa `@bitcode/auth` (`frontend/packages/auth/src/lib/`)

| Pieza | Archivo | Responsabilidad |
|---|---|---|
| `BitcodeUserClaims` | `models/user-claims.model.ts` | Claims tipados (`subject`, `userName`, `email`, `roles`, `tenantId`, `raw`) para que `@bitcode/ui` y futuros guards de F7-04 los consuman sin volver a tocar HTTP. |
| `BitcodeSessionState`/`BitcodeSessionStatus` | `models/session-state.model.ts` | Estado en memoria: `'unknown' \| 'loading' \| 'authenticated' \| 'anonymous'` + claims o `null`. |
| `BitcodeAuthConfig`, `provideBitcodeAuthConfig` | `config/auth-config.ts` | Endpoints configurables (ver sección 1), con defaults razonables. |
| `BITCODE_WINDOW` | `config/window.token.ts` | Abstracción de `window.location` inyectable -- reemplazable en tests sin depender de una navegación real (jsdom no la implementa). |
| `BitcodeSessionService` | `session/session.service.ts` | `checkSession()` (consulta `sessionEndpoint`, deduplicada si hay una resolución en curso), `markAnonymous()`, `clear()`, señales (`signal`) `status`/`claims`/`isAuthenticated`. |
| `mapToUserClaims` | `session/claims-mapper.ts` | Normaliza el JSON crudo del backend a `BitcodeUserClaims` (acepta alias comunes por campo, ver comentario en el archivo). |
| `BitcodeAuthService` | `actions/auth.service.ts` | `login(returnUrl?)` (navega a `/auth/login`, nunca `fetch`), `logout(options?)`, `logoutAllDevices(options?)` (`POST`, limpian el estado local incluso si la llamada falla). |
| `bitcodeAuthGuard` | `guards/auth.guard.ts` | `CanActivateFn`: exige sesión activa, resolviendo `checkSession()` si todavía no se conoce el estado; si no hay sesión, `login(state.url)` y deniega. |
| `bitcodeAuthInterceptor` | `interceptors/auth.interceptor.ts` | `HttpInterceptorFn`: fuerza `withCredentials: true` en toda request, y ante un 401 marca la sesión como anónima y redirige a login -- salvo que la request que falló sea contra `sessionEndpoint`/`logoutEndpoint`/`logoutAllEndpoint` (un 401 ahí es un resultado normal, no un evento de "sesión recién invalidada"). |

Todo lo anterior se exporta desde `src/index.ts` (`@bitcode/auth`).

### Por qué esto satisface "sin almacenar tokens inseguros"

- Ningún archivo de `@bitcode/auth` referencia `localStorage`/`sessionStorage`/`document.cookie`. La
  búsqueda es verificable: `grep -rn "localStorage\|sessionStorage\|document.cookie" frontend/packages/auth/src/lib`
  no devuelve resultados fuera de comentarios explicativos y de los tests que verifican su ausencia.
- La única persistencia de sesión del lado del navegador es la cookie `bc-bff-session`, **HttpOnly**
  (invisible para JavaScript por diseño -- ni siquiera el propio código de `@bitcode/auth` podría leerla
  si quisiera) y gestionada enteramente por el navegador vía `withCredentials`.
- Lo único que persiste en memoria de JS es el `signal` de `BitcodeSessionState` (`status` + claims ya
  resueltos) -- nunca un token, y se pierde en cada recarga completa de página (se debe volver a llamar
  `checkSession()`), consistente con no tener ninguna persistencia propia de sesión.
- `src/lib/no-token-storage.spec.ts` es evidencia automatizada explícita de este punto (ver sección 4).

## 3. Cómo integrarlo en una app (`apps/shell` u otra)

```ts
// app.config.ts
import { ApplicationConfig } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { provideBitcodeAuthConfig, bitcodeAuthInterceptor } from '@bitcode/auth';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBitcodeAuthConfig({
      sessionEndpoint: '/bff/session', // ver sección 5: pendiente real hasta que el backend lo exponga
    }),
    provideHttpClient(withInterceptors([bitcodeAuthInterceptor])),
    provideRouter(routes),
  ],
};
```

```ts
// app.routes.ts
import { Routes } from '@angular/router';
import { bitcodeAuthGuard } from '@bitcode/auth';

export const routes: Routes = [
  { path: 'pedidos', canActivate: [bitcodeAuthGuard], loadComponent: () => import('./pedidos/pedidos.page') },
];
```

```ts
// cualquier componente
import { inject } from '@angular/core';
import { BitcodeAuthService, BitcodeSessionService } from '@bitcode/auth';

export class MiComponente {
  private readonly auth = inject(BitcodeAuthService);
  private readonly session = inject(BitcodeSessionService);

  readonly isAuthenticated = this.session.isAuthenticated;
  readonly claims = this.session.claims;

  logout(): void {
    this.auth.logout({ postLogoutRedirectPath: '/' });
  }
}
```

## 4. Cómo se probó ("flujo seguro probado")

Ningún host de referencia del repositorio expone hoy el mecanismo BFF/OIDC por HTTP real (ver
`docs/guia-oidc-adapter.md`: el mecanismo está probado a nivel de librería/integración .NET, pero ningún
`samples/Sample.*.Api` ni `src/BitCode.Gateway/` tiene el wiring completo corriendo -- cablear ese host
real es trabajo de backend, fuera del alcance de esta tarea de Fase 7). Siguiendo el mismo criterio que el
resto del Plan Maestro aplicó en la Fase 6 para probar contra sistemas externos sin poder correr el
sistema real completo (`TestExternalHttpServer` en Integration Hub, `TestReportingHttpServer` en
Dashboard), `@bitcode/auth` se probó contra un **servidor HTTP real** (`Node http.createServer`, no un
mock de `HttpClient`) que implementa el subconjunto del contrato documentado necesario:
`frontend/packages/auth/src/lib/testing/bff-test-double.ts` (`BffTestDouble`).

**Desviaciones deliberadas y documentadas del doble respecto del contrato real** (comentadas también en
el propio archivo):

- No fija `Secure` en la cookie: corre sobre HTTP plano en el arnés de test; un navegador real rechazaría
  una cookie `Secure` recibida por HTTP. El backend real sí la fija.
- `GET /bff/session` no existe en ningún host BFF real todavía (ver sección 1) -- es el contrato
  propuesto por este paquete, no una verificación contra un endpoint ya construido del lado backend.
- No reimplementa PKCE/intercambio de `code`/proxy YARP -- eso ya está resuelto y probado en el backend
  (F2-02/F2-03/F2-05/F2-06); el doble sólo reproduce el resultado observable desde el navegador (cookie
  de sesión + respuestas JSON).

**Suites de prueba** (Vitest, mismo runner que el resto del workspace, `frontend/packages/auth/src/lib/`):

- `session/session.service.spec.ts`: `checkSession()` resuelve `anonymous` sin sesión, `authenticated`
  con los claims reales con sesión válida, deduplicación de llamadas concurrentes, `markAnonymous()`.
- `guards/auth.guard.spec.ts`: sin sesión deniega y redirige a `/auth/login?returnUrl=...`; con sesión
  permite sin redirigir.
- `interceptors/auth.interceptor.spec.ts`: adjunta `withCredentials` a una API arbitraria; un 401 en una
  API protegida marca la sesión anónima y redirige a login; un 401 en `sessionEndpoint` NO redirige
  (evita un ciclo de redirecciones en la carga inicial de páginas públicas).
- `actions/auth.service.spec.ts`: `login()` nunca hace `fetch` (sólo navega); `logout()` invoca
  `POST /auth/logout`, limpia el estado y revoca la sesión server-side (confirmado con un
  `checkSession()` posterior); `logout()` limpia el estado local incluso si la llamada falla; `logoutAllDevices()`
  revoca todas las sesiones del sujeto.
- `no-token-storage.spec.ts`: evidencia explícita del criterio "sin tokens inseguros" -- confirma que
  `localStorage`/`sessionStorage` están vacíos antes y después de resolver una sesión autenticada, de
  llamar a una API protegida a través del interceptor, y de hacer logout.

**Detalle técnico relevante para quien mantenga estos tests:** Angular 22 cambió el backend por defecto
de `HttpClient` de XHR a `fetch` (`FetchBackend`, con `withXhr()` como opt-in explícito al backend
anterior). El `fetch` global de Node (usado por Vitest+jsdom) no implementa un cookie jar -- a diferencia
de un navegador real, donde `fetch` con `credentials: 'include'` sí envía/recibe cookies correctamente.
Los tests de este paquete usan `provideHttpClient(withXhr())` explícitamente porque jsdom **sí**
implementa un cookie jar real sobre `XMLHttpRequest` (verificado experimentalmente antes de escribir la
suite: mismo origen + `SameSite=Strict` + `HttpOnly` se comportan igual que en un navegador real). Esto es
una particularidad del arnés de test, no una limitación de producción: en un navegador real, cualquiera
de los dos backends de `HttpClient` maneja cookies de sesión correctamente.

Comando: `npx nx run auth:test` (o `npx nx run-many -t build test lint`, que corre los 8 proyectos del
workspace, incluido `auth`).

## 5. Pendientes reales, no resueltos por esta tarea

- **No existe ningún host BFF real corriendo `/bff/session`** (ni ningún endpoint "quién soy"): agregarlo
  a `Shared.Infrastructure.Web/Security/Bff/` (o a un sample nuevo, p. ej. "Sample.Bff.Api") es trabajo de
  backend fuera del alcance de F7-03. Hasta entonces, `@bitcode/auth` está probado contra un simulador
  fiel del contrato propuesto, no contra el sistema real de punta a punta.
- **CSRF:** `docs/guia-oidc-adapter.md` no documenta ningún mecanismo explícito de protección CSRF para
  el proxy BFF (ni un header custom, ni doble-submit cookie). La cookie de sesión usa `SameSite=Strict`,
  que mitiga la mayoría de los envíos cross-site de formularios/enlaces, pero no es una protección CSRF
  completa por sí sola (p. ej. no cubre ataques same-site desde un subdominio comprometido, y algunos
  navegadores/flujos degradan `SameSite=Strict` en ciertos escenarios de navegación top-level). No se
  implementó nada del lado cliente para esto porque el contrato del backend no define qué mecanismo
  esperar (headers, cookie de doble envío, token de formulario) -- inventar uno sin verificarlo contra el
  servidor real generaría una falsa sensación de protección. **Brecha real, pendiente de una decisión
  explícita de backend** (candidata natural para F2-03/F2-06 o una tarea de seguridad dedicada).
- **Renovación silenciosa de sesión:** igual que en el backend (`docs/guia-oidc-adapter.md`, "Qué queda
  fuera de alcance de F2-03/F2-06"), `@bitcode/auth` no implementa ningún mecanismo de renovación
  silenciosa -- una sesión vencida simplemente redirige a `/auth/login` vía el interceptor.
- **Autorización RBAC/ABAC:** `bitcodeAuthGuard` sólo resuelve "¿hay sesión sí/no?" -- guards/directivas
  conscientes de roles/permisos son F7-04, explícitamente fuera de esta tarea.
- **`mapToUserClaims` es una heurística de "mejor esfuerzo":** el esquema real de claims que devolvería
  un `/bff/session` real depende del IdP configurado (Keycloak u otro) y de cómo el backend decida
  serializarlos -- no hay un contrato cerrado todavía (ver sección 1). `raw` conserva todo el JSON crudo
  como vía de escape mientras tanto.
