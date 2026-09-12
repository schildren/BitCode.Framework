# Guía — `@bitcode/documents` (F7-10, Fase 7 — Plataforma Angular empresarial)

> Tarea de origen: F7-10 (Documents UI) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md). Alcance:
> carga segura de archivos con progreso REAL, listado de versiones y descarga segura -- envuelve
> exactamente los endpoints de `DocumentsEndpointRouteBuilderExtensions`
> (`src/Platform/BitCode.Platform.Documents`, ver `docs/guia-documents.md` para el detalle del backend). NO
> incluye visor de contenido embebido (PDF/imagen inline) ni un explorador tipo "carpetas" -- ver sección 5.

## 1. Dónde vive cada pieza

| Pieza | Ubicación |
|---|---|
| `DocumentoResponse`/`DocumentoVersionResponse`/`EstadoEscaneo` | `frontend/packages/documents/src/lib/models/documento.model.ts` |
| `DocumentsService` (crear/subirVersion/obtener/listar/listarVersiones/descargar/disponer) | `frontend/packages/documents/src/lib/documents.service.ts` |
| `downloadFile` (dispara el guardado de un `Blob` en el navegador) | `frontend/packages/documents/src/lib/download-file.ts` |
| `DocumentUpload` (componente, selector `lib-document-upload`) | `frontend/packages/documents/src/lib/upload/document-upload.ts` (+ `.html`/`.scss`) |
| `DocumentVersionList` (componente, selector `lib-document-version-list`) | `frontend/packages/documents/src/lib/version-list/document-version-list.ts` (+ `.html`/`.scss`) |
| `DocumentsTestServer` (doble de infraestructura de PRUEBA real, no exportado del paquete) | `frontend/packages/documents/src/lib/testing/documents-test-server.ts` |

Todo lo público (salvo `DocumentsTestServer`, mismo criterio que `WorkflowTestServer`/
`InMemoryGridDataSource`/`ProblemDetailsTestServer`/`BffTestDouble`) se exporta desde
`frontend/packages/documents/src/index.ts` (`@bitcode/documents`).

## 2. Carga con progreso REAL: `DocumentsService.crear`/`.subirVersion`

```ts
export type DocumentUploadProgress =
  | { readonly status: 'uploading'; readonly percent: number }
  | { readonly status: 'done'; readonly id: string };
```

Ambos métodos usan `HttpClient` con `{ reportProgress: true, observe: 'events' }` sobre un `FormData` real
(multipart, igual que espera `[FromForm]`/`IFormFile` del backend) -- **nunca** un progreso simulado con un
`setInterval`. `percent` se calcula de `HttpEvent.loaded`/`.total` reales del navegador. El observable emite
`{ status: 'uploading', percent }` repetidas veces (creciendo) y termina con un único
`{ status: 'done', id }`:

- `crear(...)`: `id` es el `Guid` del **documento** creado (`201 Created`, `POST /api/v1/documentos`).
- `subirVersion(documentoId, archivo)`: `id` es el `Guid` de la **nueva versión** (`201 Created`,
  `POST /api/v1/documentos/{id}/versiones`) -- el documento ya se conoce (es el parámetro `documentoId`).

`DocumentUpload` (`lib-document-upload`) es el componente de consumo directo, con `[mode]="'crear'"` o
`[mode]="'version'"` (este último requiere `[documentId]`), una barra de progreso (`role="progressbar"`,
`aria-valuenow`) y el mismo criterio de error de F7-06/F7-07/F7-08/F7-09 (`BitcodeUiError`, nunca un mensaje
inventado). No reutiliza `BitcodeDynamicForm` (`@bitcode/forms`, F7-08) a propósito: el envío es multipart,
no JSON, y `BitcodeFormSubmitHandler<T>.submit` no modela progreso, sólo éxito/error.

## 3. Descarga segura: `DocumentsService.descargar` + `downloadFile`

```ts
descargar(id: string, numero?: number): Observable<{ blob: Blob; nombreArchivo: string }>
```

`responseType: 'blob'` real, `observe: 'response'` para leer `Content-Disposition` y devolver el nombre de
archivo que decidió el backend (`Results.File(..., nombreArchivo)`), no uno inventado en el cliente.
`numero` ausente descarga la versión vigente (`GET /api/v1/documentos/{id}/descargar`, sin `numero`).

`downloadFile(blob, nombreArchivo)` (`download-file.ts`) dispara el guardado real vía un `<a>` temporal +
`URL.createObjectURL`/`revokeObjectURL` -- separado deliberadamente de `DocumentsService.descargar` para
que un consumidor pueda hacer algo distinto con el blob (previsualizar, verificar) antes de guardarlo.

`DocumentVersionList` (`lib-document-version-list`) combina ambos: lista `DocumentsService.listarVersiones`
y ofrece "Descargar" por fila, **deshabilitado** cuando `puedeDescargarse(version.estadoEscaneo)` es falso
(ver sección 4) -- defensa en profundidad en la UI; la autoridad real de rechazar con 409 sigue siendo el
backend (`DocumentoVersion.PuedeDescargarse`).

## 4. Escaneo antivirus: `EstadoEscaneo`

```ts
export enum EstadoEscaneo { PendienteEscaneo = 0, Limpio = 1, Infectado = 2 }
export function puedeDescargarse(estado: EstadoEscaneo): boolean; // sólo true para Limpio
```

Toda `DocumentoVersion` nace `PendienteEscaneo` -- **ningún consumidor puede asumir que un archivo recién
cargado ya fue analizado** (mismo criterio que el backend, ver `docs/guia-documents.md`: no hay integración
real con un motor antivirus todavía, pero el flujo de negocio que depende de este estado sí está completo y
probado). `DocumentVersionList` usa `puedeDescargarse` para decidir si el botón "Descargar" está habilitado
por fila -- una versión `Infectado` nunca se ofrece para descarga en la UI.

## 5. Cómo se probó

`DocumentsTestServer` (`src/lib/testing/documents-test-server.ts`) es un servidor HTTP real (Node `http`)
que reproduce el contrato de Documents verificado contra el código del backend -- mismo patrón que
`WorkflowTestServer`. No parsea el `multipart/form-data` completo (innecesario para verificar el
comportamiento del cliente: progreso real + manejo de respuesta/error; el parser multipart del propio
backend .NET ya tiene sus pruebas de integración) -- sólo drena el body y responde según ruta/id. 21 tests
con Vitest:

- `documento.model.spec.ts`: `estadoEscaneoLabel`/`puedeDescargarse` puras.
- `download-file.spec.ts`: `URL.createObjectURL`/`revokeObjectURL` y el click del `<a>` temporal, con el
  `<a>` removido del DOM después.
- `documents.service.spec.ts` (10 casos): `crear`/`subirVersion` reportan progreso real (`toArray()` sobre
  el stream de eventos) y terminan con el id correcto; `obtener`/`listar`/`listarVersiones` contra
  respuestas reales; `descargar` recupera blob + nombre de archivo real de `Content-Disposition`, y
  propaga 409 para una versión no descargable; `disponer` resuelve 204 y propaga 409 en conflicto de
  retención.
- `document-upload.spec.ts` (componente, 3 casos): sube en `mode="crear"`/`mode="version"` con un `<input
  type="file">` real (`dispatchEvent(new Event('change'))`), emite `uploaded` con el id correcto; no
  permite subir sin archivo seleccionado.
- `document-version-list.spec.ts` (componente, 4 casos): carga versiones reales, `puedeDescargarse` refleja
  el estado real de cada versión, `descargar()` trae el blob y dispara el `<a>`, y un error de RED real
  (servidor detenido y reiniciado dentro del propio test) deja el componente en `status() === 'error'` con
  `kind: 'network'`, `reload()` reintenta con éxito.

Comandos ejecutados:

```bash
cd frontend
npx nx test documents
npx nx lint documents
npx nx build documents
npx nx run-many -t build test lint --skip-nx-cache
```

Resultado: 5 archivos de test, 21 tests pasando; `documents:lint` sin errores (2 warnings preexistentes de
`no-non-null-assertion` en un spec, mismo patrón tolerado en el resto de la Fase 7); `documents:build`
compila el entry point `@bitcode/documents` sin errores; los 8 proyectos del workspace (`core`, `auth`,
`ui`, `grid`, `forms`, `workflow`, `documents`, `shell`) pasan `build`/`test`/`lint` sin regresiones.

## 6. Limitaciones y pendientes explícitos (fuera de alcance de F7-10)

- **Sin visor de contenido embebido** (PDF/imagen inline en el navegador): toda descarga se materializa
  como un archivo guardado (`Content-Disposition: attachment`, nunca `inline` -- decisión del propio
  backend para no dejar que el navegador renderice contenido potencialmente hostil, ver
  `DocumentsEndpointRouteBuilderExtensions`).
- **Sin verificación de `hashSha256` en el cliente:** el campo se expone (`DocumentoVersionResponse`) pero
  este paquete no recalcula el hash del blob descargado para compararlo -- mismo pendiente documentado del
  lado del backend (`docs/guia-documents.md`, "Pendientes": la verificación activa en el camino de lectura
  queda pendiente).
- **Sin reintento automático de una carga interrumpida** (pausar/resumir una subida grande): `HttpClient`
  no soporta resumable uploads nativamente; una carga que falla a la mitad debe reiniciarse desde cero
  (seleccionar el archivo y subir de nuevo).
- **Sin selección de escaneo pendiente con polling** (refrescar automáticamente el estado de una versión
  `PendienteEscaneo` hasta que quede `Limpio`/`Infectado`): el consumidor debe llamar `reload()` de
  `DocumentVersionList` manualmente.
- **Sin listado de documentos con grid/paginación UI propia** (`@bitcode/grid`, F7-07): `DocumentsService
  .listar` ya devuelve la forma paginada real; conectar una `BitcodeGridDataSource<DocumentoResponse>` es
  trivial (mismo patrón que `TaskInboxHttpDataSource` de `@bitcode/workflow`) pero no se incluyó un data
  source dedicado en este primer corte porque el criterio de aceptación de F7-10 ("Archivos grandes y
  errores controlados") se centra en carga/descarga, no en el listado tabular.
- **Sin disposición desde la UI** (`DocumentsService.disponer` existe y está probado, pero no hay un
  componente de confirmación/baja en este primer corte).
- **Sin accesibilidad auditada formalmente** (F7-12): se usaron atributos ARIA básicos (`role="progressbar"`,
  `role="alert"`, `role="status"`) por buena práctica, sin correr ninguna herramienta de auditoría.
- **`DocumentsTestServer` no se exporta desde `@bitcode/documents`:** es infraestructura de prueba, mismo
  criterio que el resto de los dobles de infraestructura de F7-0x.
