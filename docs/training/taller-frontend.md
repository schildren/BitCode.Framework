# Taller de Frontend — De cero a consumir un endpoint real con `@bitcode/grid`/`@bitcode/forms`

**Audiencia:** desarrolladores frontend nuevos en BitCode.Framework.
**Objetivo de aprendizaje:** al terminar este taller vas a poder clonar el workspace Angular/Nx, entender
la estructura de los 7 paquetes `@bitcode/*`, y construir una página que lista datos de un endpoint real
con `BitcodeGrid`, los crea con `BitcodeDynamicForm`, y maneja errores de forma consistente con
`BitcodeErrorExperienceService` — sin reinventar ninguno de esos tres mecanismos.

**Duración estimada:** 90-120 minutos.

**Nota honesta sobre cómo se construyó este taller:** a diferencia de `taller-backend.md` (re-ejecutado de
punta a punta durante la preparación de F10-09), este taller se construyó **componiendo pasos y ejemplos
ya verificados con evidencia real** en las tareas de origen de cada pieza (comandos ejecutados y
resultados de test citados textualmente en `docs/guia-frontend-workspace.md`,
`docs/guia-frontend-errores.md`, `docs/guia-frontend-grid.md`, `docs/guia-frontend-forms.md`) — no se
volvió a correr el ciclo completo `npm ci && nx run-many` como una sesión nueva para esta tarea puntual.
Si algo de este taller no funciona tal cual está escrito contra el estado real del workspace, el paso
exacto que falló y por qué es información valiosa: reportalo para corregir este documento (mismo criterio
de honestidad que `docs/revision-documental-fase10.md`).

## 0. Prerrequisitos concretos

- **Node.js** — el workspace se verificó contra **Node v25.9.0**, npm 11.12.1. Angular 22.1.x/`ng-packagr`
  declaran soporte oficial para `^22.22.3 || ^24.15.0 || >=26.0.0` — Node 25.9.0 queda fuera de ese rango
  declarado, pero **funciona en la práctica** (verificado exhaustivamente en F7-01, ver
  `docs/guia-frontend-workspace.md` sección 2). Vas a ver warnings `EBADENGINE` al instalar — no bloquean
  nada, no los ignores como si fueran un error tuyo.
- Un checkout del repositorio `BitCode.Framework` (el workspace vive en `frontend/`).
- El backend de referencia corriendo, si querés probar contra un endpoint real y no solo contra datos en
  memoria (ver sección 4.1 para la alternativa sin backend). El taller de backend de esta misma carpeta
  (`taller-backend.md`) te deja un módulo real; para levantarlo como app HTTP completa necesitás
  `docs/guia-cli-diagnostico.md` (taller de operación) para confirmar que SQL Server está disponible.

### Checkpoint 0

```bash
node --version
npm --version
```

## 1. Clonar e instalar el workspace

```bash
cd frontend
npm ci
```

`npm ci` (no `npm install`) para una instalación reproducible desde el lockfile — vas a ver warnings
`EBADENGINE`/`npm audit` (vulnerabilidades en dependencias transitivas del toolchain de build, no en
código de producción de BitCode, ver `docs/guia-frontend-workspace.md` sección 8) — no bloquean el
install.

### Checkpoint 1

```bash
npm run build     # == nx run-many -t build
```

Debe compilar los 8 proyectos del workspace (7 librerías `@bitcode/*` + la app `shell`) sin error.

## 2. Entender la estructura antes de escribir código

```
frontend/
├── apps/shell/          # App Angular real (navigation shell, F7-05) -- acá vas a agregar tu página
├── packages/
│   ├── core/             # @bitcode/core     -- errores (F7-06), HTTP, logging
│   ├── auth/              # @bitcode/auth     -- sesión, guards, permisos (F7-03/F7-04)
│   ├── ui/                # @bitcode/ui       -- design tokens, menú
│   ├── grid/              # @bitcode/grid     -- grilla de datos (F7-07) -- este taller
│   ├── forms/             # @bitcode/forms    -- formularios dinámicos (F7-08) -- este taller
│   ├── workflow/          # @bitcode/workflow -- bandeja de tareas (fuera de alcance de este taller)
│   └── documents/         # @bitcode/documents -- carga de archivos (fuera de alcance de este taller)
```

Cada paquete es una librería Angular publicable/buildable (`ng-packagr`) con su propio `test`/`lint`/
`build` vía Nx. Regla importante para este taller: **ninguno de los paquetes `@bitcode/*` conoce ninguna
entidad de negocio concreta** (ni "Elemento", ni "Producto", ni nada de tu dominio) — `BitcodeGrid` y
`BitcodeDynamicForm` son genéricos; vos aportás la config (columnas, campos) y el `DataSource`/
`SubmitHandler` concretos, siempre en tu propia app (`apps/shell` en este taller), nunca dentro de
`packages/grid`/`packages/forms`.

### Checkpoint 2

```bash
npx nx graph
```

Abre el grafo de dependencias del workspace en el navegador — confirmá que podés ver los 8 proyectos.

## 3. El contrato de errores que vas a reutilizar (`@bitcode/core`, F7-06)

Antes de tocar la grilla o el formulario, entendé esto: **ningún componente de este taller inventa su
propio mensaje de error**. `BitcodeErrorExperienceService.fromHttpError(error)` es el único lugar que
traduce un error HTTP crudo (`ProblemDetails`/`ValidationProblemDetails`/una excepción 500 sin `detail`) a
algo mostrable (`BitcodeUiError`: `userMessage` del catálogo, `technicalDetail`, `correlationId` si existe,
`fieldErrors` si es de validación). `BitcodeGrid` y `BitcodeDynamicForm` (secciones 4 y 5) ya lo reutilizan
internamente — vos solo necesitás registrar el interceptor una vez:

```ts
// apps/shell/src/app/app.config.ts
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { bitcodeErrorInterceptor } from '@bitcode/core';
import { bitcodeAuthInterceptor } from '@bitcode/auth';

// ...
providers: [
  provideHttpClient(withInterceptors([bitcodeErrorInterceptor, bitcodeAuthInterceptor])),
  // ...
]
```

**Orden importa** (ver `docs/guia-frontend-errores.md` sección 4): `bitcodeErrorInterceptor` primero en el
array (más "externo", ve la respuesta/error último) para que `bitcodeAuthInterceptor` (más interno) siga
siendo el único responsable de reaccionar a un 401 real antes de que el error se mapee a un
`BitcodeUiError` genérico.

### Checkpoint 3

Si `apps/shell` no tenía ninguna llamada HTTP real antes de este taller, no hay nada que verificar todavía
— el checkpoint real llega en la sección 4, cuando la grilla haga su primer pedido y falle a propósito
(backend apagado) para confirmar que el estado de error se muestra.

## 4. Listar datos con `BitcodeGrid` (F7-07)

`BitcodeGrid` **nunca** pagina/ordena/filtra en el cliente sobre un array completo — vos implementás
`BitcodeGridDataSource<T>.loadPage(request)`, que arma el pedido HTTP real:

```ts
import { Component, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  BitcodeGrid, BitcodeGridColumn, BitcodeGridDataSource,
  BitcodeGridPageRequest, BitcodeGridPageResult,
} from '@bitcode/grid';

interface ElementoRow {
  readonly id: string;
  readonly nombre: string;
}

class ElementosDataSource implements BitcodeGridDataSource<ElementoRow> {
  constructor(private readonly http: HttpClient) {}

  loadPage(request: BitcodeGridPageRequest): Observable<BitcodeGridPageResult<ElementoRow>> {
    // Mapea 1:1 al contrato PageRequest/PagedResult<T> del backend .NET (Shared.Kernel) -- ver
    // docs/guia-frontend-grid.md sección 2.1: "page" es 1-based en ambos lados, sin traducir nada.
    return this.http.get<BitcodeGridPageResult<ElementoRow>>('/api/v1/inventario/elementos', {
      params: { page: request.page, pageSize: request.pageSize },
    });
  }
}

@Component({
  selector: 'app-elementos-page',
  imports: [BitcodeGrid],
  template: `
    <lib-bitcode-grid [columns]="columns" [dataSource]="dataSource()" [pageSize]="20" />
  `,
})
export class ElementosPage {
  private readonly http = inject(HttpClient);
  readonly dataSource = signal<BitcodeGridDataSource<ElementoRow>>(new ElementosDataSource(this.http));
  readonly columns: BitcodeGridColumn<ElementoRow>[] = [
    { id: 'nombre', header: 'Nombre', accessor: (e) => e.nombre, sortable: true, filterable: true },
  ];
}
```

Fijate que el endpoint (`/api/v1/inventario/elementos`) es el mismo módulo `Inventario` del taller de
backend, pero usando su endpoint YA EXISTENTE de listado paginado (`ListarElementosQuery`, no el
`/activos` que agregaste en ese taller — ese devuelve una lista sin paginar, `BitcodeGridDataSource`
necesita un `BitcodeGridPageResult<T>`, así que usá el endpoint que ya pagina).

### 4.1 Prototipar sin backend (`InMemoryGridDataSource`)

Si todavía no tenés el backend corriendo, `InMemoryGridDataSource` (paquete de prueba, ver
`docs/guia-frontend-grid.md` sección 4.1) pagina/ordena/filtra de verdad sobre un array en memoria con el
mismo contrato — reemplazá `new ElementosDataSource(this.http)` por
`new InMemoryGridDataSource(elementosDePrueba, { getFieldValue, globalFilterFields: ['nombre'] })` para
avanzar con el resto del taller sin depender de infraestructura.

### Checkpoint 4

```bash
npx nx serve shell
```

Navegá a la página que agregaste. Deberías ver la grilla cargando (`status() === 'loading'`), y luego
filas reales o el estado vacío. **Apagá el backend a propósito** (o usá una URL inexistente) y confirmá que
la grilla muestra el estado de error con un `userMessage` del catálogo (no un stacktrace ni un mensaje en
inglés de Angular) y un botón "Reintentar" — esto confirma que el interceptor de la sección 3 está
funcionando de punta a punta.

## 5. Crear datos con `BitcodeDynamicForm` (F7-08)

`BitcodeDynamicForm` renderiza un `FormGroup` que VOS construís (no lo crea el componente):

```ts
import { Component, inject } from '@angular/core';
import { FormControl, FormGroup } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { BitcodeDynamicForm, BitcodeFormFieldConfig, BitcodeFormSubmitHandler, buildFieldValidators } from '@bitcode/forms';

interface CrearElementoForm {
  readonly nombre: string;
}

const FIELDS: BitcodeFormFieldConfig[] = [
  { name: 'nombre', label: 'Nombre', validators: { required: true, minLength: 3 } },
];

class CrearElementoSubmitHandler implements BitcodeFormSubmitHandler<CrearElementoForm> {
  constructor(private readonly http: HttpClient) {}
  submit(value: CrearElementoForm): Observable<unknown> {
    return this.http.post('/api/v1/inventario/elementos', value);
  }
}

@Component({
  selector: 'app-crear-elemento-page',
  imports: [BitcodeDynamicForm],
  template: `
    <lib-bitcode-dynamic-form
      [fields]="fields" [form]="form" [submitHandler]="submitHandler"
      submitLabel="Crear elemento" (submitted)="onCreated()"
    />
  `,
})
export class CrearElementoPage {
  private readonly http = inject(HttpClient);
  readonly fields = FIELDS;
  readonly form = new FormGroup({
    nombre: new FormControl('', buildFieldValidators(FIELDS[0].validators)),
  });
  readonly submitHandler = new CrearElementoSubmitHandler(this.http);
  onCreated(): void { /* navegar, refrescar la grilla de la sección 4, etc. */ }
}
```

`CrearElementoCommand` del backend (taller de backend, generado por `dotnet new bitcode-module`) valida
`Nombre` con `NotEmpty().MaximumLength(100)` vía FluentValidation — si el backend rechaza el POST con 400
(`ValidationProblemDetails`), `BitcodeDynamicForm` mapea automáticamente el error al campo `nombre` (
`applyServerFieldErrors`, ver `docs/guia-frontend-forms.md` sección 4) porque `"Nombre"` (PascalCase del
backend) colapsa a `"nombre"` (camelCase) por la convención por defecto — coincide exactamente con el
nombre de tu `FormControl`.

### Checkpoint 5

1. Enviá el formulario vacío → debe mostrar el error client-side ("Este campo es obligatorio" o
   equivalente del catálogo) SIN llamar al backend.
2. Enviá un nombre válido con el backend apagado → debe mostrar el banner de error general (`userMessage`
   del catálogo, `correlationId` o "no disponible", nunca un mensaje inventado).
3. Con el backend corriendo, enviá un nombre válido → el formulario debe volver a `idle` y emitir
   `submitted`.

## 6. Problemas comunes

| Problema | Causa | Solución |
|---|---|---|
| `npm ci` imprime `npm warn EBADENGINE` para `ng-packagr`/`@angular/cli` | Node instalado (ej. 25.x) fuera del rango `engines` declarado por Angular 22.1.x. | Es un warning, no un error — verificado que el workspace funciona igual (`docs/guia-frontend-workspace.md` sección 2). Si en el futuro empieza a FALLAR (no solo advertir), fijar Node a una LTS soportada (`22.22.3+`/`24.15.0+`). |
| La grilla nunca sale de `status() === 'loading'` | El endpoint no devuelve la forma `BitcodeGridPageResult<T>` esperada (`items`/`totalCount`, ver `docs/guia-frontend-grid.md` sección 2), o la URL no matchea ninguna ruta versionada (`/api/v1/...` — regla dura #20 de `docs/convenciones.md`, sin versión el backend responde 404 de ruteo). | Confirmá con una herramienta HTTP (curl/Postman) la forma real de la respuesta antes de asumir un bug del componente. |
| El error de campo del backend (`Nombre`) no se marca en el `FormControl` (`nombre`) | Tu `FormGroup` usa un nombre de control que no coincide con la transformación por defecto (primer segmento en minúscula) — por ejemplo, un campo anidado (`Direccion.Calle`) o un nombre de control que no sigue la convención. | Proveé tu propio `resolveServerFieldName` (ver `docs/guia-frontend-forms.md` sección 4.2) en vez de asumir que el mapeo por defecto siempre alcanza. |
| `correlationId` siempre aparece como "no disponible" | Hallazgo honesto y documentado, no un bug: el backend hoy NO agrega ningún correlation id a un `Result.Failure` de negocio (400/404/409) — solo un 500 no controlado expone `traceId` (ver `docs/guia-frontend-errores.md` sección 2). | No hay nada que arreglar del lado frontend; es un pendiente de una tarea de backend futura. |
| `BitcodeGrid`/`BitcodeDynamicForm` no aparecen tipados al importar desde `@bitcode/grid`/`@bitcode/forms` | El paquete no está buildeado (`dist/`) y la resolución de `npm workspaces` symlinkea a `packages/<paquete>/` fuente, no a `dist/` — normalmente no es un problema en desarrollo (Nx resuelve por `tsconfig.base.json`), pero si corriste `npm ci` sin un build previo puede faltar algún artefacto generado (p. ej. tokens de `@bitcode/ui`, ver `docs/guia-frontend-workspace.md` sección 7.2). | Corré `npm run build` (`nx run-many -t build`) una vez antes de `nx serve shell` si ves errores de tipos "no encontrados". |

## 7. Qué sigue después de este taller

- Para permisos/ocultar columnas o campos según el rol del usuario (`requiredPermissions`), ver
  `docs/guia-frontend-grid.md` sección 3 y `docs/guia-frontend-forms.md` sección 6 (reutilizan
  `@bitcode/auth`, F7-04).
- Para manejar sesión/login real contra el BFF, ver `docs/guia-frontend-auth.md` — noten la limitación
  honesta documentada ahí: el endpoint `GET /bff/session` que `@bitcode/auth` necesita todavía no existe
  en ningún host real de BitCode (sección 1 de esa guía).
- Para operar el frontend en producción (build reproducible, versionado, publicación), ver
  `docs/guia-frontend-workspace.md` secciones 4 y 6.
