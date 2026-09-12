# Guía — Menú dinámico / Navigation shell (F7-05, Fase 7 — Plataforma Angular empresarial)

> Tarea de origen: F7-05 (Menú dinámico) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
> Alcance: modelo de items de navegación, filtrado reactivo por permisos reutilizando F7-04
> (`@bitcode/auth`), y un componente de navegación real en `apps/shell` que reemplaza el placeholder de
> F7-01. No incluye páginas/rutas reales de ningún módulo de negocio (eso es trabajo de F7-09/F7-10 y
> tareas futuras), ni un diseño visual elaborado (eso es fuera de alcance de F7-05; se consumen los
> design tokens de F7-02 con un layout mínimo).

## 1. Dónde vive cada pieza, y por qué

| Pieza | Ubicación | Motivo |
|---|---|---|
| `BitcodeMenuItem` (modelo) | `frontend/packages/ui/src/lib/navigation/menu-item.model.ts` | Genérico: no conoce ningún módulo de negocio concreto de BitCode, sólo la forma de un item de navegación (label, link, ícono, orden, permiso(s), hijos anidados). |
| `filterMenuByPermissions` (función pura) | `frontend/packages/ui/src/lib/navigation/menu-filter.ts` | Filtra un árbol de `BitcodeMenuItem` según los claims del actor, reutilizando `hasRequiredPermissions` de `@bitcode/auth` (F7-04) -- no reimplementa ningún chequeo de permisos propio. |
| `BITCODE_MENU_ITEMS` / `provideBitcodeMenuItems` | `frontend/packages/ui/src/lib/navigation/menu.config.ts` | Token de configuración para que cada app registre su propio árbol de items, igual patrón que `provideBitcodeAuthConfig` de `@bitcode/auth`. |
| `BitcodeMenuService` | `frontend/packages/ui/src/lib/navigation/menu.service.ts` | Deriva `menu` (árbol visible) con `computed()` a partir de `BitcodeSessionService.claims()` -- ver sección 3 para la interpretación de "actualización controlada". |
| `SHELL_MENU_ITEMS` (configuración concreta) | `frontend/apps/shell/src/app/navigation/shell-menu.config.ts` | Los 12 módulos de plataforma de Fase 6 (Identity Administration, Organization, Catalogs, Feature Management, Documents, Workflow, Task Inbox, Notifications, Integration Hub, Import and Export, Reporting, Dashboard) como árbol de EJEMPLO -- esta app sí sabe qué módulos existen, `@bitcode/ui` no. |
| `NavigationShell` (componente) | `frontend/apps/shell/src/app/navigation/navigation-shell.ts` (+ `.html`/`.scss`) | Renderiza `BitcodeMenuService.menu()` como sidebar con grupos expandibles/colapsables, usando tokens `--bc-*` de F7-02. |

**Criterio de esta división** (pedido explícitamente por la tarea): el MODELO/servicio de menú es
genérico y vive en un paquete de plataforma (`@bitcode/ui`, ya existente como "design system y
componentes base" -- no se creó un paquete nuevo porque el menú dinámico es, en esencia, un componente de
UI reutilizable más, no un dominio propio del tamaño de `@bitcode/grid`/`@bitcode/workflow`); la
CONFIGURACIÓN concreta con los módulos reales de BitCode vive en la app `shell`, que es la única pieza que
sabe qué módulos existen y en qué rutas.

`@bitcode/ui` ahora declara `@bitcode/auth` como `peerDependency` (`packages/ui/package.json`): es la
primera dependencia real entre paquetes `@bitcode/*` del workspace. Sigue sin haber `depConstraints` de
Nx definidos (heredado de F7-01/F7-02, ver `docs/guia-frontend-workspace.md` sección 8) -- se podría
definir ahora que existe un caso real (`ui` → `auth`), pero eso es una decisión de gobierno del grafo de
dependencias más amplia que esta tarea puntual; se deja como pendiente explícito, no resuelto acá.

## 2. Modelo de menú: árbol, no lista con `parentId`

Se eligió modelar `BitcodeMenuItem.children` como árbol anidado en vez de una lista plana con
`parentId` + reconstrucción en el componente: un menú lateral empresarial rara vez pasa de 2-3 niveles de
profundidad, y un árbol evita tener que reconstruirlo cada vez que se resuelve. Cada item admite:

- `id` (estable, usado por ejemplo para recordar qué grupos están expandidos).
- `label`, `link` (opcional -- ausente en un grupo puro sin página propia), `icon` (opcional, sólo nombre
  de ícono como string; F7-05 no incluye una librería de íconos concreta, ver limitaciones).
- `order` (opcional; sin `order` el item se ubica al final, en orden estable).
- `requiredPermissions` (`string | string[]`) + `permissionMode` (`'all' | 'any'`, mismo contrato que
  `hasRequiredPermissions`/`*bitcodeHasPermission` de F7-04) -- convención `"{entidad}.{accion}"`.
- `children` (opcional, mismo tipo recursivamente).

## 3. Filtrado por permisos: reglas y reutilización de F7-04

`filterMenuByPermissions(items, claims)` (función PURA, sin dependencias de Angular) es la única lógica
de filtrado, y delega el chequeo de un permiso puntual a `hasRequiredPermissions` de `@bitcode/auth` --
la MISMA función que ya usan `bitcodeRequirePermissionGuard` y `*bitcodeHasPermission` (F7-04). Reglas:

- Un item con `requiredPermissions` que el actor no satisface se excluye por completo, junto con todos
  sus `children` (no tiene sentido ofrecer sub-items de un padre no autorizado).
- Un item sin `requiredPermissions` es visible en sí mismo; si es un grupo puro (sin `link` propio) cuyos
  `children` quedan TODOS filtrados, tampoco se muestra -- evita un grupo vacío en la navegación (criterio
  pedido explícitamente en el enunciado de la tarea).
- Un grupo CON `link` propio se muestra igual aunque todos sus hijos queden filtrados (tiene una página
  propia que mostrar, sólo pierde sus sub-items).
- El resultado se ordena por `order` ascendente (estable para items sin `order`).

Es la misma advertencia de F7-04 ("la UI no sustituye validación backend"): ocultar un item de menú es
exclusivamente una conveniencia de UX. Un actor que edite el bundle, manipule el DOM, o navegue
directamente a una URL oculta en el menú, de todos modos depende de que esa ruta esté protegida
server-side (o por un guard de F7-04 en `app.routes.ts`, no implementado en este alcance porque F7-05 no
cablea rutas reales -- ver sección 5).

## 4. "Actualización controlada" — interpretación explícita del criterio de aceptación

El Plan Maestro no detalla más este criterio. Interpretación adoptada, documentada explícitamente como
pide el enunciado de la tarea:

> El menú debe recalcularse de forma predecible y reactiva cuando cambian los claims/permisos del actor
> (login, logout, una sesión distinta detectada por `checkSession()`), sin necesidad de recargar la
> página completa -- pero SIN refrescos/parpadeos innecesarios en emisiones intermedias del estado de
> sesión (p. ej. mientras `status` pasa transitoriamente por `'loading'` al reconfirmar la MISMA sesión ya
> conocida).

Implementación: `BitcodeMenuService.menu` es un `computed()` que depende ÚNICAMENTE de
`BitcodeSessionService.claims()` (nunca de `status()` ni de un mecanismo de eventos/suscripción manual
propio). Esto es correcto y suficiente porque `BitcodeSessionService.checkSession()` (F7-03) ya está
escrito de forma que, al re-confirmar una sesión sin cambios, el `signal` `claims` conserva la MISMA
referencia de objeto durante la fase `'loading'` intermedia
(`this.state.set({ status: 'loading', claims: this.state().claims })`): la semántica estándar de Angular
signals es que un `computed()` sólo se re-evalúa cuando una señal de la que depende cambia de valor (no
cuando el `signal` contenedor se vuelve a `set()` con contenido `Object.is`-igual) -- así que
`BitcodeMenuService.menu` ni siquiera se recalcula durante esa fase intermedia, sin que este paquete haya
tenido que implementar ninguna lógica de debounce/memoización propia.

Verificado explícitamente en
`frontend/packages/ui/src/lib/navigation/menu.service.spec.ts` (caso "no hay parpadeo a menú anónimo
mientras se reconfirma la misma sesión ya conocida"): se llama a `checkSession()` una segunda vez sobre
una sesión ya autenticada y se comprueba, de forma SÍNCRONA (antes de que la promesa resuelva), que
`menu()` sigue devolviendo la MISMA referencia de array que antes de la llamada.

## 5. Componente de navegación (`apps/shell`)

`NavigationShell` (`frontend/apps/shell/src/app/navigation/`) renderiza `BitcodeMenuService.menu()` como
lista de items de nivel superior; los grupos (`children` no vacío) se renderizan como un botón
expandible/colapsable (`aria-expanded`, ícono de chevron rotado), y los items hoja como `routerLink`
(marcado activo con `routerLinkActive`). Reemplaza el placeholder `NxWelcome` de F7-01 en `App`
(`frontend/apps/shell/src/app/app.html`) -- éste era exactamente el punto en el que F7-01 documentó que
"F7-05 la completa" (ver `docs/guia-frontend-workspace.md`, sección 3).

Estilos (`navigation-shell.scss`) consumen exclusivamente custom properties `--bc-*` generadas por F7-02
(`--bc-space-*`, `--bc-color-surface`, `--bc-color-border`, `--bc-color-brand-primary-hover`,
`--bc-color-brand-primary-text`, `--bc-font-family-sans`, `--bc-font-size-sm`, `--bc-font-weight-semibold`,
`--bc-radius-md`, `--bc-duration-fast`), sin valores de color/espaciado hardcodeados.

`apps/shell/src/app/app.config.ts` ahora registra (además del router):

- `provideBitcodeAuthConfig()` (F7-03) y `provideHttpClient(withInterceptors([bitcodeAuthInterceptor]))`
  (F7-03/F7-04) -- necesarios porque `BitcodeMenuService`/`NavigationShell` dependen transitivamente de
  `BitcodeSessionService`, que a su vez necesita `HttpClient`. Esta app placeholder no tenía ninguno de
  los dos configurados todavía (F7-03/F7-04 se probaron a nivel de librería, no de app).
- `provideBitcodeMenuItems(SHELL_MENU_ITEMS)` (F7-05) con el árbol de los 12 módulos de Fase 6.

## 6. Simplificaciones y limitaciones honestas

- **Sin persistencia de expandido/colapsado:** `NavigationShell` guarda qué grupos están expandidos en un
  `Set<string>` en memoria del propio componente -- se pierde en cada recarga de página o navegación que
  destruya el componente. No hay `localStorage` ni preferencia de usuario persistida server-side. Elegido
  deliberadamente así para no introducir un mecanismo de persistencia sin que la tarea lo pida.
- **Sin librería de íconos real:** `BitcodeMenuItem.icon` es sólo un string (p. ej. `'home'`,
  `'admin_panel_settings'`, nombres de Material Symbols como convención de ejemplo) -- `NavigationShell`
  no lo renderiza todavía (no hay ninguna librería de íconos incluida en el workspace). El campo existe en
  el modelo para no tener que ampliar el contrato cuando se agregue una.
- **`SHELL_MENU_ITEMS` es un árbol de EJEMPLO, no un catálogo verificado:** los `requiredPermissions`
  siguen la convención `"{entidad}.{accion}"` de F7-04 por analogía razonable con cada módulo de Fase 6,
  pero NINGUNO fue verificado contra el catálogo real de permisos que cada módulo backend expone (habría
  que inspeccionar los 12 módulos uno por uno, fuera del alcance de F7-05, que es sobre el MECANISMO
  genérico, no sobre cablear el catálogo definitivo de permisos de cada módulo).
- **La mayoría de las rutas del menú siguen sin registrar en `app.routes.ts`:** F7-05 es sobre el menú en
  sí, no sobre implementar cada página de los 12 módulos. F7-14 registró tres rutas lazy de demostración
  (`/inicio`, `/procesos/documentos`, `/procesos/workflow`, ver `docs/guia-frontend-performance.md`) para
  probar code-splitting real, con páginas mínimas (no pantallas de negocio terminadas) -- el resto de los
  `link` del menú (identidad, organización, catálogos, feature management, bandeja de tareas,
  notificaciones, integraciones, reporting, dashboard) sigue sin ruta registrada. Un click en esos items
  navega a una URL sin ruta registrada (el `Router` de Angular no encuentra coincidencia y no navega, sin
  lanzar una excepción no controlada) -- comportamiento esperado y aceptable para el alcance de estas
  tareas, pero UX incompleta hasta que existan esas páginas (trabajo de una aplicación de referencia real,
  Fase 8).
- **Doble de prueba HTTP propio en `@bitcode/ui`, no reutilización directa de `BffTestDouble`:**
  `packages/ui/src/lib/navigation/testing/session-endpoint-test-double.ts` es un servidor HTTP real
  (Node `http`) que reproduce únicamente `GET /bff/session` -- un subconjunto deliberadamente mínimo del
  mismo contrato que `BffTestDouble` de `@bitcode/auth` (`packages/auth/src/lib/testing/bff-test-double.ts`).
  Se intentó primero reexportar `BffTestDouble` desde el índice público de `@bitcode/auth` para
  reutilizarlo tal cual (la tarea lo autoriza explícitamente), pero **rompía el build de producción de
  `@bitcode/auth`**: ese archivo usa `node:http`, y el `tsconfig.lib.json` de `@bitcode/auth` excluye a
  propósito `src/lib/testing/**` del build publicable (es código de test, no de navegador) y no declara
  tipos de Node. Forzar esa reexportación hubiera degradado el build de una librería ya estable de F7-03
  para ahorrar duplicar ~60 líneas de servidor HTTP de test. Se documentó la decisión en el propio archivo
  del doble.
- **`@bitcode/ui` gana su primera dependencia real de otro paquete `@bitcode/*` (`@bitcode/auth`):** no
  se definieron `depConstraints` de Nx para formalizar qué puede depender de qué (pendiente heredado de
  F7-01/F7-02, ver sección 1).

## 7. Cómo se probó

Todo con Vitest, mismo runner que el resto del workspace (`npx nx run-many -t build test lint`):

- `packages/ui/src/lib/navigation/menu-filter.spec.ts`: función pura -- sin permiso vs. con permiso, modo
  `'all'`/`'any'`, grupo vacío no se muestra, grupo con `link` propio sí se muestra vacío, corte completo
  del árbol cuando el padre no tiene permiso, orden por `order`, inmutabilidad de la entrada.
- `packages/ui/src/lib/navigation/menu.service.spec.ts`: contra un servidor HTTP real
  (`SessionEndpointTestDouble`, sección 6) y `BitcodeSessionService` real (no un mock del `signal`) --
  menú inicial sin sesión, recálculo tras `checkSession()` con permisos reales, ocultamiento tras
  `markAnonymous()`, y el caso central de "actualización controlada" (sección 4): sin parpadeo al
  reconfirmar la misma sesión.
- `apps/shell/src/app/navigation/navigation-shell.spec.ts`: componente con un doble mínimo de
  `BitcodeSessionService` (mismo criterio que `has-permission.directive.spec.ts` de F7-04) -- renderizado
  sin sesión, aparición de un grupo colapsado al tener permiso de un solo hijo, expandir/colapsar,
  visibilidad completa con todos los permisos, y que el árbol expuesto por el componente es exactamente
  el de `BitcodeMenuService` (no una copia propia).
- `apps/shell/src/app/app.spec.ts`: la app raíz renderiza `<app-navigation-shell>` y, sin sesión resuelta,
  el link "Inicio" (sin permiso) es visible y "Reportes" (con permiso) no lo es.

Comando: `npx nx run-many -t build test lint` -- verificado limpio para los 8 proyectos del workspace
(`core`, `auth`, `ui`, `grid`, `forms`, `workflow`, `documents`, `shell`) tras este cambio.
