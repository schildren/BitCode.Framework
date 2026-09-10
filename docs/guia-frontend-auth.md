# Guía — `@bitcode/auth` (F7-03 + F7-04, Fase 7 — Plataforma Angular empresarial)

> Tareas de origen: F7-03 (Autenticación) y F7-04 (Autorización UI) del
> [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
> Alcance: sesión, login/logout, guards de sesión y de permisos, directivas de autorización estructural,
> claims tipados (incluidos permisos RBAC) e interceptor HTTP del paquete `frontend/packages/auth/`
> (`@bitcode/auth`). Se implementó todo en el mismo paquete de F7-03 en vez de crear uno nuevo: F7-04
> extiende directamente el mismo modelo de sesión/claims (`BitcodeSessionService`, `BitcodeUserClaims`)
> que ya expone `@bitcode/auth`, no un concepto separado -- partir la autorización en un paquete aparte
> hubiera obligado a esa segunda librería a re-suscribirse al mismo `signal` de sesión sin ganar nada a
> cambio.

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
| `BitcodeUserClaims` | `models/user-claims.model.ts` | Claims tipados (`subject`, `userName`, `email`, `roles`, `permissions` [F7-04], `tenantId`, `raw`) para que `@bitcode/ui` y los guards/directivas de F7-04 los consuman sin volver a tocar HTTP. |
| `BitcodeSessionState`/`BitcodeSessionStatus` | `models/session-state.model.ts` | Estado en memoria: `'unknown' \| 'loading' \| 'authenticated' \| 'anonymous'` + claims o `null`. |
| `BitcodeAuthConfig`, `provideBitcodeAuthConfig` | `config/auth-config.ts` | Endpoints configurables (ver sección 1), con defaults razonables. |
| `BITCODE_WINDOW` | `config/window.token.ts` | Abstracción de `window.location` inyectable -- reemplazable en tests sin depender de una navegación real (jsdom no la implementa). |
| `BitcodeSessionService` | `session/session.service.ts` | `checkSession()` (consulta `sessionEndpoint`, deduplicada si hay una resolución en curso), `markAnonymous()`, `clear()`, señales (`signal`) `status`/`claims`/`isAuthenticated`. |
| `mapToUserClaims` | `session/claims-mapper.ts` | Normaliza el JSON crudo del backend a `BitcodeUserClaims` (acepta alias comunes por campo, ver comentario en el archivo). |
| `BitcodeAuthService` | `actions/auth.service.ts` | `login(returnUrl?)` (navega a `/auth/login`, nunca `fetch`), `logout(options?)`, `logoutAllDevices(options?)` (`POST`, limpian el estado local incluso si la llamada falla). |
| `bitcodeAuthGuard` | `guards/auth.guard.ts` | `CanActivateFn`: exige sesión activa, resolviendo `checkSession()` si todavía no se conoce el estado; si no hay sesión, `login(state.url)` y deniega. |
| `bitcodeAuthInterceptor` | `interceptors/auth.interceptor.ts` | `HttpInterceptorFn`: fuerza `withCredentials: true` en toda request, y ante un 401 marca la sesión como anónima y redirige a login -- salvo que la request que falló sea contra `sessionEndpoint`/`logoutEndpoint`/`logoutAllEndpoint` (un 401 ahí es un resultado normal, no un evento de "sesión recién invalidada"). |
| `hasRequiredPermissions` | `permissions/permission-checks.ts` | (F7-04) `true`/`false` según si unos claims satisfacen uno o varios permisos, con semántica `'all'` (AND, default) o `'any'` (OR). Base común del guard y la directiva de abajo. |
| `bitcodeRequirePermissionGuard` | `permissions/require-permission.guard.ts` | (F7-04) Fábrica de `CanActivateFn` parametrizada por permiso(s): sin sesión redirige a login (igual que F7-03); con sesión pero sin el permiso redirige a `unauthorizedPath`. |
| `BitcodeHasPermissionDirective` (`*bitcodeHasPermission`) | `permissions/has-permission.directive.ts` | (F7-04) Directiva estructural: muestra/oculta el elemento según permiso(s) del actor, reactiva al `signal` de sesión. |
| `getActorAttribute` | `abac/actor-attributes.ts` | (F7-04) Lectura tipada de atributos ABAC del ACTOR (tenant u otros claims de alcance) -- ver sección 5.4 para el alcance honesto de ABAC-cliente. |
| `BitcodeIfActorDirective` (`*bitcodeIfActor`) | `abac/if-actor.directive.ts` | (F7-04) Directiva estructural GENÉRICA basada en predicado (`(claims) => boolean`) para condiciones de alcance que dependen de un recurso concreto que sólo el componente conoce. |

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

## 5. Autorización UI (F7-04): RBAC/ABAC del lado cliente

Todo lo de esta sección vive en `frontend/packages/auth/src/lib/permissions/` y
`frontend/packages/auth/src/lib/abac/`, y se exporta desde el mismo `@bitcode/auth`.

### 5.1 Modelo de permisos del lado cliente

`BitcodeUserClaims.permissions` (`models/user-claims.model.ts`) expone los permisos del actor como
`readonly string[]`, con la MISMA convención `"{entidad}.{accion}"` que usa el backend
(`RequirePermissionAttribute`, `IdentityAdministrationPermissions`, Fase 2/6). `mapToUserClaims`
(`session/claims-mapper.ts`) acepta los alias `permissions`/`permission` en el JSON crudo de
`sessionEndpoint`, con el mismo criterio de "mejor esfuerzo" que ya aplicaba a `roles`/`tenantId`.

**Nota honesta, igual que el resto de F7-03:** ningún host BFF real publica hoy los permisos resueltos
del usuario en `/bff/session` -- es, otra vez, un contrato PROPUESTO por este paquete (`BffTestDouble`
extendido para soportarlo en los tests), no verificado contra un endpoint real. Si el campo no viene,
`permissions` resuelve como array vacío, nunca `undefined` ni una excepción.

### 5.2 Guard consciente de permisos: `bitcodeRequirePermissionGuard`

```ts
import { Routes } from '@angular/router';
import { bitcodeRequirePermissionGuard } from '@bitcode/auth';

export const routes: Routes = [
  {
    path: 'usuarios/nuevo',
    canActivate: [bitcodeRequirePermissionGuard('identidad.usuarios.crear')],
    loadComponent: () => import('./usuarios/crear-usuario.page'),
  },
  {
    // options.mode: 'all' (default, AND) exige TODOS los permisos listados; 'any' (OR) alcanza con uno.
    path: 'usuarios/:id/roles',
    canActivate: [
      bitcodeRequirePermissionGuard(['identidad.usuarios.roles.asignar'], { mode: 'all' }),
    ],
    loadComponent: () => import('./usuarios/asignar-rol.page'),
  },
];
```

Comportamiento: si NO hay sesión, se comporta exactamente como `bitcodeAuthGuard` (redirige a
`loginPath` vía `window.location`). Si HAY sesión pero falta algún permiso requerido, redirige (vía
`Router`, navegación interna de la SPA, nunca `window.location`) a `BitcodeAuthConfig.unauthorizedPath`
(default `/unauthorized`) -- una ruta distinta de login a propósito, porque "sesión válida sin permiso" y
"sin sesión" son dos situaciones distintas para el usuario y no tiene sentido reenviarlo a autenticarse
otra vez.

### 5.3 Directiva estructural: `*bitcodeHasPermission`

```html
<button *bitcodeHasPermission="'identidad.usuarios.crear'" (click)="crear()">Crear usuario</button>

<!-- múltiples permisos, semántica explícita por `mode` (default 'all' = AND) -->
<button *bitcodeHasPermission="['identidad.usuarios.crear', 'identidad.usuarios.roles.asignar']; mode: 'any'">
  ...
</button>
```

Se recalcula reactivamente ante cualquier cambio del `signal` `BitcodeSessionService.claims` (login,
logout, refresco de sesión), sin que el componente que la usa tenga que suscribirse a nada manualmente.

### 5.4 ABAC del lado cliente: alcance deliberadamente limitado

El backend (`AttributeScopeAbacRule`, `src/Shared.Infrastructure.Security/Abac/`) compara un atributo del
ACTOR contra un atributo del RECURSO concreto (p. ej. "¿la sucursal del pedido #123 está entre las
sucursales del actor?"). Un guard de ruta o una directiva reutilizable NO conocen el recurso concreto de
antemano (la ruta es `/pedidos/:id`, no "el pedido de la sucursal Norte") -- replicar la regla fielmente
en un mecanismo genérico sería, en el mejor de los casos, una implementación que aparenta funcionar sin
poder ser correcta con la información realmente disponible del lado cliente en ese punto.

Por eso el alcance de esta primera versión es deliberadamente acotado a dos piezas:

1. `getActorAttribute(claims, key)` (`abac/actor-attributes.ts`): lee de forma tipada un atributo del
   ACTOR (tenant u otro claim de alcance publicado en la sesión) -- la mitad de la ecuación ABAC que el
   cliente sí conoce sin depender del recurso.
2. `*bitcodeIfActor` (`abac/if-actor.directive.ts`): directiva estructural GENÉRICA que recibe un
   PREDICADO (`(claims: BitcodeUserClaims | null) => boolean`), no una regla hardcodeada -- para que un
   componente que YA conoce el recurso concreto (porque lo cargó) exprese su propia condición de alcance:

   ```html
   <!-- el componente ya cargó `pedido()` (con su `sucursalId`) -->
   <button *bitcodeIfActor="esDeMiSucursal">Aprobar pedido</button>
   ```
   ```ts
   esDeMiSucursal = (claims: BitcodeUserClaims | null) => claims?.tenantId === this.pedido().sucursalId;
   ```

Esto NO es "ABAC completo en el cliente" -- es la porción que es honesto implementar sin conocer el
recurso, más una vía de escape genérica para el resto. Como todo lo demás en `@bitcode/auth`, es
exclusivamente una conveniencia de UX: la regla de alcance real sigue evaluándose server-side en cada
request, con el estado actual del recurso, no con el estado que el cliente tenía en memoria al pintar el
botón.

### 5.5 "La UI no sustituye validación backend" (criterio de aceptación de F7-04)

`bitcodeRequirePermissionGuard`, `*bitcodeHasPermission` y `*bitcodeIfActor` documentan explícitamente
(mismo tono que `bitcodeAuthGuard` de F7-03) que son EXCLUSIVAMENTE conveniencias de UX: ocultan
elementos o evitan navegaciones que de todos modos fallarían contra el backend, pero nunca son la única
línea de defensa. Un actor que edite el bundle en el navegador, manipule el DOM, o llame al backend
directamente (consola, `curl`, Postman, un cliente propio que no use el Router de Angular) sortea estos
mecanismos por completo.

Esto está verificado, no sólo documentado: `permissions/require-permission.guard.spec.ts` agrega un
endpoint protegido por permiso al mismo `BffTestDouble` (`POST /bff/api/usuarios`, devuelve 403 real si
falta `identidad.usuarios.crear`) y dos casos explícitos:

- Un actor sin el permiso, al que el guard correctamente le niega la navegación, sigue recibiendo un
  **403 real del servidor** si llama al servicio HTTP DIRECTAMENTE (sin pasar por el guard/Router) --
  demostrando que la protección real no depende de que la UI la haya evitado.
- El mismo actor CON el permiso concedido server-side sí obtiene una respuesta exitosa -- confirmando que
  el 403 anterior es autorización real (el servidor la evalúa), no un endpoint roto o un doble mal
  configurado.

### 5.6 Tests (F7-04)

Todos con Vitest, mismo runner (`npx nx run auth:test`):

- `permissions/permission-checks.spec.ts`: `hasRequiredPermissions` -- sin claims deniega, permiso
  exacto, falta de permiso, semántica `'all'` (AND) y `'any'` (OR).
- `permissions/require-permission.guard.spec.ts`: sin sesión redirige a login; con sesión sin permiso
  redirige (Router) a `unauthorizedPath`; con permiso permite; múltiples permisos en ambos modos; y el
  caso crítico de la sección 5.5 (403 real independiente del guard, en ambos sentidos).
- `permissions/has-permission.directive.spec.ts`: oculta/muestra según permiso (único y múltiple, ambos
  modos), reacciona en vivo a cambios de sesión (login/logout), usando un doble mínimo de
  `BitcodeSessionService` (sólo el `signal` `claims` -- la resolución HTTP real ya está cubierta en otras
  suites).
- `abac/actor-attributes.spec.ts` y `abac/if-actor.directive.spec.ts`: lectura tipada de atributos del
  actor y comportamiento del predicado genérico (incluye un ejemplo de predicado no ligado a `tenantId`,
  para evidenciar que no es una regla fija).
- `session/session.service.spec.ts` (extendido): `checkSession()` mapea `permissions` cuando el backend
  los publica, y resuelve array vacío cuando no.

## 6. Pendientes reales, no resueltos por esta tarea

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
- **`mapToUserClaims` es una heurística de "mejor esfuerzo":** el esquema real de claims que devolvería
  un `/bff/session` real depende del IdP configurado (Keycloak u otro) y de cómo el backend decida
  serializarlos -- no hay un contrato cerrado todavía (ver sección 1). `raw` conserva todo el JSON crudo
  como vía de escape mientras tanto.
- **Permisos en `/bff/session` (F7-04):** igual que el resto del contrato de sesión, ningún host BFF real
  publica hoy los permisos resueltos del usuario -- `BitcodeUserClaims.permissions` es, otra vez, un
  contrato PROPUESTO por este paquete. Agregar la resolución de permisos del actor al endpoint real
  "quién soy" (cuando exista) es trabajo de backend, no de esta tarea de Fase 7.
- **ABAC del lado cliente (F7-04) es deliberadamente parcial:** no reimplementa `AttributeScopeAbacRule`
  ni ninguna otra regla de alcance dinámica del backend -- ver sección 5.4 para el razonamiento completo.
  Sólo provee lectura tipada de atributos del actor y un mecanismo GENÉRICO basado en predicado
  (`*bitcodeIfActor`) para que componentes futuros, que sí conocen el recurso concreto, expresen su
  propia condición. No hay ningún catálogo de reglas de alcance del lado cliente, ni lo habrá mientras el
  cliente no tenga forma de conocer el recurso de antemano en un mecanismo genérico.
- **Página/componente "no autorizado" (`unauthorizedPath`, default `/unauthorized`) no está implementado
  en ningún host de referencia:** `bitcodeRequirePermissionGuard` sólo define y usa la ruta configurada;
  crear la pantalla real (mensaje, navegación de vuelta, etc.) es responsabilidad de la app consumidora o
  de una tarea posterior de UX (candidata natural para F7-06, "Errores").
- **Invalidación de permisos en caliente:** si el backend revoca un permiso, el cliente sigue mostrando el
  estado de la última resolución de sesión hasta el próximo `checkSession()` (login, recarga, o un 401 que
  dispare el interceptor) -- no hay un mecanismo de push/revalidación periódica. Es exactamente la misma
  limitación, y el mismo motivo, por el que esto nunca reemplaza la validación server-side (ver sección
  5.5): la última palabra siempre es la respuesta HTTP real del backend en el momento de la operación.
