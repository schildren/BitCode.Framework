# Guía — `@bitcode/grid` (F7-07, Fase 7 — Plataforma Angular empresarial)

> Tarea de origen: F7-07 (Grilla) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md). Alcance:
> grilla de datos empresarial GENÉRICA (no conoce ninguna entidad de negocio concreta) con paginación,
> ordenamiento y filtrado siempre server-side, virtualización de filas y columnas configurables. NO
> incluye selección de filas, export (CSV/Excel), edición inline, agrupamiento, ni i18n real de fechas/
> moneda -- ver sección 6 para el detalle honesto de lo que queda fuera de este primer corte.

## 1. Dónde vive cada pieza

| Pieza | Ubicación |
|---|---|
| `BitcodeGrid` (componente, selector `lib-bitcode-grid`) | `frontend/packages/grid/src/lib/grid/grid.ts` (+ `.html`/`.scss`) |
| `BitcodeGridColumn<T>` (config de columna) | `frontend/packages/grid/src/lib/models/grid-column.model.ts` |
| `BitcodeGridPageRequest`/`BitcodeGridPageResult<T>`/`BitcodeGridSort`/`BitcodeGridFilterState` | `frontend/packages/grid/src/lib/models/grid-request.model.ts` |
| `BitcodeGridDataSource<T>` (contrato que implementa el consumidor) | `frontend/packages/grid/src/lib/data-source/grid-data-source.model.ts` |
| `InMemoryGridDataSource<T>` (data source de PRUEBA real, no exportado del paquete) | `frontend/packages/grid/src/lib/testing/in-memory-grid-data-source.ts` |

Todo lo público (salvo `InMemoryGridDataSource`, ver sección 4) se exporta desde
`frontend/packages/grid/src/index.ts` (`@bitcode/grid`).

## 2. El contrato central: `BitcodeGridDataSource<T>`

```ts
export interface BitcodeGridDataSource<T> {
  loadPage(request: BitcodeGridPageRequest): Observable<BitcodeGridPageResult<T>>;
}
```

`BitcodeGrid` **nunca** pagina/ordena/filtra sobre un array completo cargado en el cliente: arma un
`BitcodeGridPageRequest` a partir de su propio estado (`page`, `pageSize`, `sort`, `filter`) y renderiza
tal cual el `BitcodeGridPageResult<T>` que devuelve el data source. Quien implementa `loadPage` es
responsable de aplicar esas tres operaciones contra el backend real (o, en un prototipo sin backend
todavía, contra datos en memoria -- ver sección 4).

### 2.1 Paginación: por número de página (offset), no por cursor

```ts
export interface BitcodeGridPageRequest {
  readonly page: number;      // 1-based
  readonly pageSize: number;
  readonly sort: BitcodeGridSort | null;
  readonly filter: BitcodeGridFilterState;
}
```

**Decisión y por qué:** se alinea deliberadamente con el contrato YA EXISTENTE del backend .NET
(`Shared.Kernel.PageRequest`/`PagedResult<T>`, usado tal cual por TODOS los `Listar*Query` de la Fase 6 --
p. ej. `ListarCatalogosQuery(Page, PageSize)` en `BitCode.Platform.Catalogs`, verificado leyendo el código
real antes de diseñar esto, no asumido). `page` es 1-based, igual que `PageRequest.Create(page, pageSize)`
del backend -- un data source que llame a un endpoint real de BitCode mapea este objeto directamente a los
query params `page`/`pageSize` sin traducir nada.

**Alternativa descartada, documentada explícitamente:** paginación por cursor (`nextCursor` opaco) es
preferible para datasets que cambian de tamaño entre lecturas de páginas consecutivas (evita "saltos"/
duplicados cuando se insertan/borran filas entre una página y la siguiente), pero **ningún endpoint real
de BitCode expone hoy un cursor** -- inventar ese contrato sin un backend que lo respalde hubiera sido,
otra vez, un supuesto no verificable (mismo criterio que se aplicó en `@bitcode/auth` para no inventar
contratos de backend inexistentes). Si en el futuro un módulo de alto volumen (p. ej. un feed de auditoría)
necesita paginación por cursor, la solución es un `BitcodeGridDataSource<T>` alternativo que internamente
traduzca `page`/`pageSize` a su propio cursor (o, si el patrón de acceso no lo permite en absoluto, una
extensión de este contrato en una tarea posterior) -- no un cambio del contrato de la grilla en sí para
este primer corte.

### 2.2 Ordenamiento y filtrado: siempre server-side

```ts
export interface BitcodeGridSort {
  readonly columnId: string;
  readonly direction: 'asc' | 'desc';
}

export interface BitcodeGridFilterState {
  readonly global?: string;                          // barra de búsqueda libre
  readonly columns: Readonly<Record<string, string>>; // sólo columnas con filterable: true
}
```

- **Un único orden a la vez** (no multi-sort): un tercer click sobre el mismo encabezado vuelve a "sin
  ordenamiento explícito" (ciclo `asc -> desc -> sin orden`, no queda atascado en `desc`). Multi-sort
  (ordenar por más de una columna simultáneamente) queda fuera de alcance -- ver sección 6.
- **Filtro global + filtro por columna, combinables:** el data source recibe ambos en el mismo
  `BitcodeGridFilterState.filter` y decide cómo combinarlos (AND es lo esperable, pero es una decisión del
  data source/backend, no de la grilla).
- **Los filtros NO se aplican tecla por tecla:** el input de filtro (global o por columna) sólo actualiza
  un "borrador" local (`globalFilterDraft`/`columnFilterDraft`) -- el pedido real al data source se dispara
  al presionar Enter o el botón "Buscar" (`applyFilters()`). Filtrar en cada tecla sin una capa de debounce
  (RxJS `debounceTime` o un timer manual) generaría un pedido server-side por carácter tipeado; se prefirió
  no construir esa capa de debounce en este primer corte antes que construirla mal -- ver limitación en la
  sección 6.
- Cambiar de orden o confirmar un filtro siempre reinicia `page` a `1` (seguir en la página 7 de un
  resultado que cambió de forma no tiene sentido para el usuario).

## 3. Configuración de columnas (`BitcodeGridColumn<T>`)

Análogo a `BitcodeMenuItem`/`menu.config.ts` de `@bitcode/ui` (F7-05): datos puros y tipados, sin
acoplarse a ninguna entidad de negocio.

```ts
export interface BitcodeGridColumn<T> {
  readonly id: string;
  readonly header: string;
  readonly accessor: (item: T) => unknown;
  readonly formatter?: (value: unknown, item: T) => string;
  readonly type?: 'text' | 'number' | 'date' | 'boolean' | 'custom';
  readonly width?: string;                 // '160px', '2fr', etc. -- default '1fr'
  readonly sortable?: boolean;
  readonly filterable?: boolean;
  readonly requiredPermissions?: string | readonly string[];
  readonly permissionMode?: 'all' | 'any'; // default 'all', igual que hasRequiredPermissions
}
```

**Columnas ocultas por permiso** reutilizan EXACTAMENTE el mismo mecanismo que `filterMenuByPermissions`
de `@bitcode/ui` (`hasRequiredPermissions`/`@bitcode/auth`, F7-04) -- no una comparación de permisos propia.
`BitcodeSessionService` se inyecta de forma **opcional** (`inject(BitcodeSessionService, { optional: true
})`): una grilla que no necesita ocultar columnas por permiso no está obligada a tener `@bitcode/auth`
provisto en el árbol de inyectores de la app; sin sesión inyectada, cualquier columna con
`requiredPermissions` queda oculta (mismo criterio que el resto de la autorización UI: "sin sesión
resuelta" nunca se trata como "autorizado por defecto").

## 4. Ejemplo mínimo de consumo

```ts
import { Component, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  BitcodeGrid,
  BitcodeGridColumn,
  BitcodeGridDataSource,
  BitcodeGridPageRequest,
  BitcodeGridPageResult,
} from '@bitcode/grid';

interface UsuarioRow {
  readonly id: string;
  readonly nombre: string;
  readonly email: string;
  readonly activo: boolean;
}

/** Implementación mínima de un data source respaldado por HTTP. El backend real decide cómo interpretar
 * `sort`/`filter` (p. ej. mapear `sort.columnId` a un `ORDER BY` conocido, `filter.global` a un `LIKE`) --
 * la grilla sólo envía el pedido tal cual. */
class UsuariosDataSource implements BitcodeGridDataSource<UsuarioRow> {
  constructor(private readonly http: HttpClient) {}

  loadPage(request: BitcodeGridPageRequest): Observable<BitcodeGridPageResult<UsuarioRow>> {
    return this.http.get<BitcodeGridPageResult<UsuarioRow>>('/api/usuarios', {
      params: {
        page: request.page,
        pageSize: request.pageSize,
        ...(request.sort ? { sortBy: request.sort.columnId, sortDir: request.sort.direction } : {}),
        ...(request.filter.global ? { q: request.filter.global } : {}),
        ...request.filter.columns,
      },
    });
  }
}

@Component({
  selector: 'app-usuarios-page',
  imports: [BitcodeGrid],
  template: `
    <lib-bitcode-grid
      [columns]="columns"
      [dataSource]="dataSource()"
      [pageSize]="25"
      [pageSizeOptions]="[10, 25, 50, 100]"
    />
  `,
})
export class UsuariosPage {
  private readonly http = inject(HttpClient);

  readonly dataSource = signal<BitcodeGridDataSource<UsuarioRow>>(new UsuariosDataSource(this.http));

  readonly columns: BitcodeGridColumn<UsuarioRow>[] = [
    { id: 'nombre', header: 'Nombre', accessor: (u) => u.nombre, sortable: true, filterable: true },
    { id: 'email', header: 'Email', accessor: (u) => u.email, sortable: true, filterable: true },
    {
      id: 'activo',
      header: 'Activo',
      accessor: (u) => u.activo,
      formatter: (value) => (value ? 'Sí' : 'No'),
      width: '120px',
      requiredPermissions: 'identidad.usuarios.ver-estado',
    },
  ];
}
```

Un `HttpClient` real con `bitcodeErrorInterceptor` (`@bitcode/core`, F7-06) registrado ya propaga un
`BitcodeHttpError` con el `BitcodeUiError` calculado -- `BitcodeGrid` lo reutiliza TAL CUAL en su estado de
error (ver sección 5), sin volver a mapearlo.

### 4.1 Prototipar sin backend: `InMemoryGridDataSource`

`InMemoryGridDataSource<T>` (`src/lib/testing/in-memory-grid-data-source.ts`) es un data source de PRUEBA
REAL -- aplica de verdad paginación/ordenamiento/filtrado sobre un array en memoria, con el mismo contrato
que uno respaldado por HTTP -- usado en las specs de `BitcodeGrid` y sirve de referencia para prototipar
una pantalla sin backend todavía:

```ts
import { InMemoryGridDataSource } from '@bitcode/grid/testing'; // ver nota de exportación abajo

const dataSource = new InMemoryGridDataSource(usuarios, {
  getFieldValue: (usuario, columnId) => usuario[columnId as keyof UsuarioRow],
  globalFilterFields: ['nombre', 'email'],
});
```

**Nota honesta:** `InMemoryGridDataSource` **NO se exporta** desde `@bitcode/grid` (`src/index.ts`) --
mismo criterio que `BffTestDouble`/`ProblemDetailsTestServer` de otros paquetes de F7-0x: es infraestructura
de prueba/documentación, no una utilidad de producción. El fragmento de arriba (`from '@bitcode/grid/testing'`)
es ilustrativo del USO, no un import que funcione hoy tal cual -- para usarlo fuera del propio paquete
`@bitcode/grid` habría que decidir explícitamente exportarlo (o copiar el patrón), lo cual queda fuera de
alcance de esta tarea.

## 5. Estados de carga/vacío/error

`BitcodeGrid` expone (señales de sólo lectura de facto, usadas también por su propio template):

- `status(): 'idle' | 'loading' | 'loaded' | 'error'`
- `items()`, `totalCount()`, `page()`, `sort()`
- `error(): BitcodeUiError | null`
- `isEmpty()`: `true` cuando `status() === 'loaded'` y `items().length === 0` (un filtro sin resultados, no
  un error).

**Error, reutilizando F7-06 tal cual, nunca un mensaje inventado en este componente:**

```ts
private toUiError(error: unknown): BitcodeUiError {
  if (error instanceof BitcodeHttpError) {
    return error.uiError; // ya viene mapeado por bitcodeErrorInterceptor
  }
  return this.errorExperience.fromHttpError(error); // BitcodeErrorExperienceService, mismo catálogo
}
```

El estado de error del template muestra `userMessage` (catálogo de F7-06), el `correlationId` (o "no
disponible" -- NUNCA se omite en silencio, mismo criterio que `docs/guia-frontend-errores.md`), el
`technicalDetail` en un `<details>` colapsable, y un botón "Reintentar" (`reload()`) que repite EXACTAMENTE
el mismo `BitcodeGridPageRequest` en curso (misma página/orden/filtro), no que resetea a la página 1.

Cada nuevo pedido cancela la suscripción del anterior (`Subscription.unsubscribe()`) y descarta por número
de secuencia cualquier respuesta que llegue fuera de orden -- evita que una respuesta lenta de una página
vieja pise el resultado ya renderizado de un pedido más nuevo.

## 6. Virtualización (`@angular/cdk/scrolling`)

Se usó `@angular/cdk` **22.1.5** (instalado en esta tarea, `npm install @angular/cdk@22.1.5`), verificado
compatible con el Angular del workspace (`22.1.4`, peer range `^22.0.0 || ^23.0.0`) antes de instalar --
`cdk-virtual-scroll-viewport` + `*cdkVirtualFor` (`ScrollingModule`) renderizan sólo las filas visibles del
`items()` de la página actual (no de un dataset completo -- ver sección 2, la "ventana" ya viene acotada
por `pageSize` desde el data source). `rowHeight` (input, default `44`) y `viewportHeight` (input, default
`'480px'`) son configurables por el consumidor.

**Instalación real, no asumida:** se corrió `npm view @angular/cdk@22.1.5 peerDependencies` antes de
instalar para confirmar el rango de peer dependencies contra el Angular real del workspace, y luego
`npm install @angular/cdk@22.1.5 --save` en la raíz de `frontend/` (mismo patrón que el resto de las
dependencias del workspace, `npm workspaces`). No hubo que degradar ni forzar ninguna versión.

## 7. Cómo se probó

Vitest, mismo runner que el resto del workspace:

- `src/lib/testing/in-memory-grid-data-source.spec.ts`: el data source de prueba pagina, ordena y filtra
  DE VERDAD (páginas distintas devuelven distintos items, `totalCount` se recalcula sobre el subconjunto
  filtrado, ambas direcciones de orden), más inyección determinística de fallas (`failWhen`).
- `src/lib/grid/grid.spec.ts` (10 casos), contra un host component con `InMemoryGridDataSource` real (no
  un stub que siempre devuelve lo mismo):
  - Primera página según `pageSize`; `nextPage()` pide la página siguiente al data source (no recorta un
    array ya cargado -- se verifica que el primer id de la página 2 sea el correcto, no una porción del
    array de la página 1).
  - Ordenamiento asc/desc/sin-orden resuelto por el data source, reinicia a la página 1.
  - Filtro global y filtro por columna, ambos recalculando `totalCount` contra el subconjunto real.
  - Estado vacío (`isEmpty()`) sin error, para un filtro sin resultados.
  - Un data source que falla con un `Error` genérico se mapea con `BitcodeErrorExperienceService`
    (`kind: 'unknown'`, mensaje del catálogo, `technicalDetail` = mensaje original).
  - Un data source que falla con un `BitcodeHttpError` (F7-06) se reutiliza TAL CUAL (`toBe`, misma
    instancia de `BitcodeUiError`) -- no se vuelve a mapear.
  - `reload()` reintenta exactamente el mismo pedido tras una falla transitoria.
  - Columnas con `requiredPermissions` se ocultan/muestran reactivamente según
    `BitcodeSessionService.claims` (doble mínimo, mismo patrón que
    `has-permission.directive.spec.ts` de `@bitcode/auth`).

Comandos ejecutados:

```bash
cd frontend
npx nx run grid:test --skip-nx-cache
npx nx run grid:lint --skip-nx-cache
npx nx run grid:build --skip-nx-cache
npx nx run-many -t build test lint --skip-nx-cache
```

Resultado: 15 tests nuevos pasando en `grid` (5 del data source de prueba + 10 del componente),
`grid:lint`/`grid:build` limpios, y los 8 proyectos del workspace (`core`, `auth`, `ui`, `grid`, `forms`,
`workflow`, `documents`, `shell`) siguen pasando `build`/`test`/`lint` sin regresiones.

## 8. Limitaciones y pendientes explícitos (fuera de alcance de F7-07)

- **Sin selección de filas** (checkboxes, selección múltiple, acciones masivas): no implementado.
- **Sin export** (CSV/Excel) de los datos mostrados ni de la consulta completa: no implementado -- un
  export real de un dataset completo (no sólo la página visible) es, de hecho, un caso de uso distinto
  (más cercano a un job en background, como ya existe en `BitCode.Platform.ImportExport`) que una simple
  descarga de la grilla.
- **Sin edición inline de celdas:** la grilla es de sólo lectura/listado; formularios de edición son
  F7-08 (`@bitcode/forms`).
- **Sin multi-sort** (ordenar por más de una columna a la vez): el modelo (`BitcodeGridSort`, singular) no
  lo admite hoy; extenderlo a `readonly BitcodeGridSort[]` es un cambio de contrato futuro, no trivial de
  hacer retrocompatible si ya hay consumidores reales.
- **Filtro por columna sin debounce de tecleo:** se aplica al confirmar (Enter/botón "Buscar"), no en cada
  tecla -- ver razón en la sección 2.2. Agregar debounce real (RxJS `debounceTime` sobre los cambios de
  borrador) es una mejora incremental compatible hacia adelante, no incluida en este primer corte.
- **Sin paginación por cursor:** ver la decisión y alternativa descartada en la sección 2.1.
- **Sin persistencia de estado de la grilla** (orden/filtro/página) en la URL ni en `localStorage`: al
  recargar la página o navegar y volver, la grilla vuelve a su estado inicial. Común en grillas
  empresariales (deep-linking a una vista filtrada/ordenada) pero no pedido explícitamente por el criterio
  de aceptación de esta tarea ("datos masivos sin bloqueo").
- **Ancho de columna fijo, sin resize interactivo** (arrastrar el borde de una columna para ensancharla):
  `BitcodeGridColumn.width` es estático, configurado por el consumidor.
- **Altura de fila fija** (`rowHeight`, requerido por `cdk-virtual-scroll-viewport` con
  `itemSize`): no soporta filas de alto variable (p. ej. texto que se ajusta a varias líneas) -- es la
  estrategia de virtualización MÁS SIMPLE de `@angular/cdk/scrolling` (tamaño fijo); una estrategia de
  tamaño variable existe en el CDK pero agrega complejidad no justificada para este primer corte.
- **Sin tema oscuro probado visualmente:** los estilos usan `--bc-*` (F7-02), que sí definen valores para
  `[data-theme='dark']`, pero no se hizo una verificación visual manual de la grilla en modo oscuro (no hay
  aún ningún mecanismo de UI para alternar el tema, ver `docs/guia-frontend-workspace.md` sección 7.4).
- **`InMemoryGridDataSource` no se exporta desde `@bitcode/grid`** (ver sección 4.1): decisión deliberada,
  es infraestructura de prueba, no de producción.
- **Sin accesibilidad auditada formalmente** (F7-12): se usaron atributos ARIA básicos (`role="table"`,
  `role="row"`, `role="columnheader"`, `role="cell"`, `role="status"`/`role="alert"` para los estados de
  carga/error, `aria-label` en los inputs de filtro) por buena práctica, pero no se corrió ninguna
  herramienta de auditoría (axe, Lighthouse) contra este componente.
