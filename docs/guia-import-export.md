# Import and Export — guía de consumo (Fase 6, módulo 10)

**Tarea:** Fase 6, módulo 10 del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md) ("Import and
Export": "Validación, lotes, progreso, errores y reanudación", dependencia declarada: Documents y Events).
**Fecha:** 2026-09-09. **Estado:** Camino feliz completo, con las 5 palabras de la fila del Plan Maestro
verificadas contra SQL Server real (Testcontainers) y un filesystem real (no un mock de
`IImportExportFileStore`) — ver "Qué quedó completo y qué no" para el detalle honesto, incluida una
limitación deliberadamente NO resuelta en "reanudación de una exportación" (ver esa sección). Auditoría de
arquitectura (2026-09-09) encontró y este corte ya corrigió un bug real (el rechazo de archivos no-UTF8
válidos era código muerto, nunca rechazaba nada) al agregar la cobertura de test que la propia auditoría
señaló como faltante — ver "Validación".

---

## Ubicación

- Librería: `src/Platform/BitCode.Platform.ImportExport/` (`BitCode.Platform.ImportExport.csproj`),
  namespace raíz `BitCode.Framework.Platform.ImportExport`.
- Host de referencia: `samples/Sample.ImportExport.Api/` (incluye un tercer `DbContext` propio del host,
  `SampleClientesDbContext`, para demostrar los puntos de extensión con datos de negocio reales).
- Tests: `samples/Sample.ImportExport.Api.Tests/Integration/ImportExportEndpointsIntegrationTests.cs`,
  contra SQL Server real (Testcontainers) y un directorio temporal real en disco.

Mismo patrón librería + host + tests que los 9 módulos anteriores de Fase 6: la librería expone
`ImportExportDbContext`, `AddSharedImportExport()` y `MapImportExportEndpoints()` — nunca un
`IWebFrameworkModule` propio.

## Por qué CSV y qué tan simple es este parser

El Plan Maestro no fija un formato de archivo. Se eligió **CSV** (no JSON) porque es el formato realista
para "un usuario de negocio sube un archivo con filas de datos" — exportado directamente desde Excel/una
planilla de cálculo, sin que el usuario final escriba JSON a mano. `Csv/CsvLineParser.cs` implementa un
subconjunto acotado de RFC 4180 (comas, comillas dobles con escape `""`) — **sin soporte para un valor
entre comillas que contenga un salto de línea embebido**: un archivo con ese caso partiría en más "filas"
de las que en realidad tiene. Aceptable para el alcance de referencia de este módulo; un consumidor con
datos que legítimamente necesiten saltos de línea embebidos (una dirección multilínea, por ejemplo) debe
evaluar un parser CSV completo de terceros antes de usar este motor en producción.

## Relación con Documents — decisión explícita, NO se reutiliza `IDocumentBlobStore`

El Plan Maestro declara "Documents" como dependencia de este módulo. Se evaluaron dos opciones:

- **Opción A (elegida):** este módulo define su propia interfaz mínima de almacenamiento
  (`Almacenamiento/IImportExportFileStore.cs`) con una implementación de referencia en filesystem
  (`FileSystemImportExportFileStore`, mismo criterio de escritura atómica y protección de path traversal
  que `FileSystemDocumentBlobStore` de Documents, pero código propio e independiente). El endpoint de alta
  de una importación recibe el archivo directamente por `multipart/form-data`, nunca una referencia a un
  documento ya existente en Documents.
- **Opción B (descartada):** el cliente sube el archivo primero a la API pública de Documents, obtiene un
  `DocumentoId`, y este módulo lo lee llamando al endpoint HTTP público de descarga de Documents (como si
  fuera un sistema externo, reutilizando `AddResilientHttpClient`).

**Por qué A y no B:** B honra más literalmente la dependencia declarada del Plan Maestro, pero acopla dos
bounded contexts (este módulo pasaría a depender de que Documents esté desplegado y accesible por HTTP
solo para poder leer un archivo que en la práctica es TRANSITORIO — se sube, se procesa en unos pocos
ciclos, y no tiene ningún ciclo de vida de negocio propio más allá del `ImportJob`/`ExportJob` que lo
referencia). Documents nació precisamente porque el framework no tenía un módulo de Storage genérico para
DOCUMENTOS DE NEGOCIO con versión, antivirus y retención — ninguna de esas preocupaciones aplica al archivo
transitorio de una importación. Acoplar este módulo a la API pública de Documents solo para reutilizar
almacenamiento habría sido acoplamiento sin beneficio real, violando el criterio de aislamiento de módulos
que Task Inbox/Notifications ya aplicaron (consumir SOLO eventos públicos de Workflow, nunca su
`DbContext`) — acá el equivalente es "consumir solo el propio almacenamiento, nunca el ajeno".

**Trade-off aceptado:** este módulo NO reutiliza ninguna infraestructura de Documents (ni siquiera
antivirus) — un archivo CSV importado NO se escanea con `IAntivirusScanner`. Si un consumidor real necesita
esa protección para archivos subidos por usuarios externos, debe agregarla explícitamente (reutilizando
`BitCode.Platform.Documents.Antivirus.IAntivirusScanner` si ya tiene ese módulo instalado, o su propio
escáner) antes de `IniciarImportacionCommandHandler` — pendiente explícito, no implementado en este corte.

## Modelo de dominio

```
ImportJob (TipoImportacion, ArchivoBlobKey, FilasTotales fijado al alta)
  └── ImportJobError (0..N, append-only: NumeroFila + MensajeError + ContenidoFilaCrudo)

ExportJob (TipoExportacion, FiltroJson opcional, ArchivoResultadoBlobKey fijado al alta)
  └── ExportJobError (0..N, append-only: NumeroFila + MensajeError)
```

Ambos `Estado` recorren `Pendiente -> EnProgreso -> Completado | CompletadoConErrores | Fallido`.
`Fallido` es EXCLUSIVAMENTE para un fallo del JOB COMPLETO (archivo inaccesible, tipo sin manejador
registrado) — una fila individual con error nunca hace fallar el job completo, ver "Errores".

## Puntos de extensión pluggable — este módulo no conoce ningún modelo de negocio

Ni `ImportJob` ni `ExportJob` saben qué es un "cliente" o un "producto". La semántica de cada tipo de
importación/exportación la aporta el HOST, implementando:

- `Importacion.IImportRowHandler` — valida/aplica UNA fila ya materializada como diccionario
  columna-a-valor. `ColumnasRequeridas` declara qué columnas mínimas exige (el job las valida antes de
  llamar al handler). Debe ser IDEMPOTENTE (upsert por clave natural) — ver "Reanudación".
- `Exportacion.IExportDataSource` — devuelve páginas sucesivas de filas para un tipo de exportación,
  estables entre llamadas con el mismo `offset` (mismo motivo de idempotencia).

Ambas interfaces reciben el `tenantId` del job explícitamente como parámetro — los jobs de procesamiento
corren CROSS-TENANT (sin `HttpContext`/`ITenantProvider` resoluble, mismo motivo que
`IntegrationOutboundProcessorJob`), así que cada implementación es responsable de aplicar su propio
aislamiento multi-tenant con ese valor.

Mismo espíritu que `IIntegrationConnectorSender` (Integration Hub) o un canal de
`INotificationChannelSender` (Notifications): un punto de extensión que el host implementa y registra en
su propio contenedor de DI (`services.AddScoped<IImportRowHandler, MiHandler>()`) —
`AddSharedImportExport()` NO registra ninguna implementación concreta, solo la infraestructura genérica.

`samples/Sample.ImportExport.Api/Clientes/` contiene la implementación de referencia
(`ClientesImportRowHandler`/`ClientesExportDataSource`) sobre un `SampleClientesDbContext` propio del
host — deliberadamente fuera de `BitCode.Platform.ImportExport`, para dejar clarísimo que el framework no
conoce ese modelo.

## Lotes — un chunk por job pendiente en cada disparo, nunca el archivo completo de una vez

`Procesamiento.ImportBatchProcessorJob`/`ExportBatchProcessorJob` (Quartz, mismo patrón que
`IntegrationOutboundProcessorJob`/`NotificationRetryJob`) procesan, en CADA disparo, como máximo
`ImportExportOptions.TamanoLoteFilas` filas de CADA job pendiente — nunca el archivo completo en un único
método síncrono. Un consumidor real los registra con `AddSharedBackgroundJobs` (F4-11, Quartz HA):

```csharp
services.AddSharedBackgroundJobs(
    quartz =>
    {
        var jobKey = new JobKey("importexport-import-batch");
        quartz.AddJob<ImportBatchProcessorJob>(j => j.WithIdentity(jobKey).RequestRecovery().StoreDurably());
        quartz.AddTrigger(t => t.ForJob(jobKey).WithSimpleSchedule(s => s.WithIntervalInSeconds(5).RepeatForever()));
    },
    ha => ha.ConnectionString = connectionString);
// Mismo patrón para ExportBatchProcessorJob, con su propio JobKey.
```

El host de referencia NO los registra — se verifican en `Sample.ImportExport.Api.Tests` invocando
`ProcesarPendientesAsync` directamente, en un scope de DI nuevo cada vez (mismo criterio que
`IntegrationOutboundProcessorJob` en Integration Hub).

## Progreso — consultable en cualquier momento

`GET /api/v1/importexport/importaciones/{id}` / `.../exportaciones/{id}` devuelven `FilasTotales`,
`FilasProcesadas`/`FilasExportadas`, `FilasConError` — un cliente real puede pollear este endpoint mientras
el job avanza en background para mostrar una barra de progreso.
`ImportJob.FilasTotales` se fija UNA VEZ al alta (`Csv.CsvFileLineCounter`, una lectura completa del
archivo en memoria — ver "Límites de tamaño de archivo"). `ExportJob.FilasTotales` es `int?`: se completa
recién cuando `IExportDataSource` informa un total conocido en alguna página — algunas fuentes no pueden
calcularlo sin recorrer la fuente entera, en cuyo caso el progreso solo puede mostrar el conteo absoluto
sin porcentaje.

## Errores — aislamiento por ítem desde el diseño inicial, en DOS niveles

Aplicado desde el primer commit de este módulo (mismo criterio ya establecido proactivamente por
Integration Hub, módulo 9 — no como corrección posterior a una auditoría):

1. **Nivel JOB:** una excepción no controlada procesando UN `ImportJob`/`ExportJob` completo (archivo
   desaparecido, tipo sin manejador/fuente registrada) nunca aborta el resto de los jobs pendientes del
   mismo ciclo — se marca `Fallido` y el `foreach` de jobs sigue.
2. **Nivel FILA:** una fila con datos inválidos, o que hace que `IImportRowHandler`/la serialización CSV de
   una fila de exportación lance una excepción no controlada, nunca aborta el resto del chunk — se registra
   un `ImportJobError`/`ExportJobError` (número de fila + mensaje) y el `while`/`for` de filas sigue.

Verificado en `Importacion_ConFilasInvalidas_AislaLosErroresYProcesaElRestoDelLote`: de 5 filas con 2
inválidas, el job termina `CompletadoConErrores` con las otras 3 aplicadas y exactamente 2 errores
reportados con su número de fila correcto.

## Reanudación — checkpoint incremental, con una limitación honesta en exportación

**Importación:** `ImportJob.UltimaFilaCheckpoint` se persiste en el MISMO `SaveChangesAsync` que las filas
de ese chunk — si el proceso muere a mitad de un chunk (antes de ese `SaveChangesAsync`), la próxima
ejecución (Quartz `RequestRecovery()`) retoma desde el último checkpoint CONFIRMADO, reprocesando como
máximo las filas del chunk interrumpido. Un `IImportRowHandler` puede recibir la misma fila más de una vez
en ese escenario (semántica "at-least-once", nunca exactly-once de punta a punta, Plan Maestro sección
3.2) — por eso la interfaz exige idempotencia (upsert por clave natural), verificado en
`Importacion_ReanudadaEnUnaInstanciaDeJobDistinta_NoReprocesaFilasYaConfirmadas`, que procesa el primer
ciclo, construye un `ImportBatchProcessorJob` COMPLETAMENTE NUEVO (otro scope de DI, otro `DbContext`) para
simular que una instancia distinta del host retoma el trabajo, y verifica que ninguna fila se duplicó.

**Exportación — limitación honesta, NO resuelta en este corte:** `ExportBatchProcessorJob` primero anexa
el chunk al archivo de resultado (`IImportExportFileStore.AppendAsync`) y RECIÉN DESPUÉS persiste el nuevo
`UltimoOffsetExportado` en `SaveChangesAsync`. Si el proceso muere ENTRE esas dos operaciones, el archivo
queda con las filas de ese chunk ya escritas pero el checkpoint sin avanzar — el próximo ciclo vuelve a
pedirle esas mismas filas a `IExportDataSource` y las vuelve a anexar, **duplicándolas en el archivo
final**. Corregirlo requeriría un checkpoint de OFFSET DE BYTE dentro del archivo de resultado (truncar
hasta ese offset antes de reintentar el chunk) en vez de un conteo de filas — no implementado en esta
primera versión. Documentado explícitamente en vez de afirmar una garantía que el código no cumple; nunca
se promete exactamente-una-vez de punta a punta (Plan Maestro, sección 3.2).

**Precisión sobre el ALCANCE real de esa ventana (auditoría de arquitectura, 2026-09-09):** la ventana de
riesgo NO es solo "el instante entre el `AppendAsync` y el `SaveChangesAsync` de ESE job" — `ExportBatchProcessorJob.ProcesarPendientesAsync`
llama `SaveChangesAsync` una única vez, DESPUÉS de recorrer TODOS los `ExportJob` pendientes del ciclo
(no uno por job). Si hay N jobs pendientes en el mismo disparo, el checkpoint de un job procesado
temprano en el lote queda sin confirmar en base de datos durante todo el tiempo que toma procesar los
N-1 jobs restantes (incluida la lectura de sus propias páginas de datos). La probabilidad real de perder
el checkpoint ante una caída es entonces mayor a la que "la línea siguiente" sugeriría — el defecto de
fondo es el mismo (ya documentado arriba), pero su ventana de exposición es más amplia.

## Validación

Dos niveles, ninguno reemplaza al otro:

1. **Al alta:** `IniciarImportacionCommandValidator` valida tamaño máximo (10 MB — ver "Límites de tamaño
   de archivo") y que el tipo de importación/exportación tenga un manejador/fuente registrada (rechazo
   temprano en vez de un job condenado a `Fallido` desde el primer ciclo, mismo criterio que
   `EnviarSolicitudIntegracionCommand` validando su conector). `IniciarImportacionCommandHandler` además
   rechaza un archivo que no sea texto UTF-8 válido (`ImportExport.Importaciones.ArchivoNoEsTextoValido`).
   **Corrección real aplicada tras auditoría de arquitectura (2026-09-09):** esta validación usaba
   `System.Text.Encoding.UTF8.GetString` (la instancia estática), que NO lanza ante bytes inválidos — usa
   un fallback de reemplazo silencioso (sustituye por U+FFFD) en vez de un fallback que lance, así que el
   `catch` era código MUERTO: cualquier archivo, sin importar su codificación real, pasaba como "texto
   válido" con caracteres corruptos en las filas afectadas, sin que nadie lo notara. Se descubrió al
   agregar el test que la propia auditoría señaló como faltante (la cobertura reveló que la validación
   nunca rechazaba nada) — corregido construyendo una instancia propia de `UTF8Encoding` con
   `throwOnInvalidBytes: true`, verificado en `IniciarImportacion_ConContenidoNoUtf8Valido_Retorna400`.
2. **Por fila, durante el procesamiento:** el job valida que las `ColumnasRequeridas` del handler estén
   presentes y no vacías ANTES de invocarlo (clasificado como error de esa fila, sin llamar al handler);
   el handler valida cualquier regla de negocio adicional (formato de email, rangos, etc.) y devuelve
   `Result.Failure` si corresponde.

## Endpoints (`/api/v1/importexport/...`)

| Método | Ruta | Permiso | Notas |
|---|---|---|---|
| POST | `/importaciones` | `importexport.importaciones.iniciar` | `multipart/form-data`: `archivo` + `tipoImportacion`, auditada |
| GET | `/importaciones/{id}` | `importexport.importaciones.ver` | Progreso |
| GET | `/importaciones/{id}/errores` | `importexport.importaciones.ver` | |
| GET | `/importaciones` | `importexport.importaciones.ver` | Filtros: `tipoImportacion`, `estado`, paginado |
| POST | `/exportaciones` | `importexport.exportaciones.iniciar` | JSON: `tipoExportacion` + `filtroJson` opcional, auditada |
| GET | `/exportaciones/{id}` | `importexport.exportaciones.ver` | Progreso |
| GET | `/exportaciones/{id}/descargar` | `importexport.exportaciones.ver` | 409 si el job no finalizó |
| GET | `/exportaciones` | `importexport.exportaciones.ver` | Filtros: `tipoExportacion`, `estado`, paginado |

## RBAC — sin ownership adicional, mismo criterio que Integration Hub

Un `ImportJob`/`ExportJob` es un dato operacional de una operación masiva sobre datos de negocio, no un
dato personal de un usuario final — el permiso RBAC por sí solo es el control correcto, sin una capa
adicional de "solo quien lo inició puede verlo". `IniciadoPorUserId` se conserva solo para
trazabilidad/auditoría.

## Auditoría

`IniciarImportacionCommand`/`IniciarExportacionCommand` escriben una entrada vía `IAuditWriter` (F2-15) —
iniciar una operación masiva que muta (importación) o lee en volumen (exportación) datos de negocio es
sensible. Consultar el progreso no se audita (mismo criterio que el resto de Fase 6: consultas de solo
lectura no auditadas).

## Sin `IHasConcurrencyToken`, decisión explícita (no un olvido)

A diferencia de `IntegrationRequest`/`Notification`, `ImportJob`/`ExportJob` tienen un ÚNICO escritor
posible: el propio `ImportBatchProcessorJob`/`ExportBatchProcessorJob`. Quartz en modo clustered (F4-11)
garantiza que un mismo trigger no corre concurrentemente en dos nodos, y este módulo no ofrece hoy ningún
comando de cancelación/edición manual de un job que pudiera competir con el procesamiento. Si un consumidor
real agrega esa capacidad, en ESE momento corresponde agregar el token de concurrencia — ver "Pendientes".

## Límites de tamaño de archivo

`IniciarImportacionCommandValidator` rechaza archivos de más de 10 MB — este módulo lee el archivo COMPLETO
en memoria una vez al alta (para contar sus filas con `CsvFileLineCounter`) y `ImportBatchProcessorJob`
reabre el archivo desde el principio y avanza línea por línea hasta el checkpoint en CADA ciclo (costo
O(filas ya procesadas) por ciclo, no O(1)) — aceptable para el tamaño de referencia de este módulo, pero no
para archivos de millones de filas con miles de ciclos. Un consumidor con archivos grandes debería: (a)
subir un offset de BYTE como checkpoint en vez de un conteo de líneas, evitando el re-scan; y/o (b) contar
filas en streaming en vez de cargar el archivo completo en memoria al alta.

## Qué quedó completo y qué no

| Capacidad (Plan Maestro) | Estado | Evidencia |
|---|---|---|
| Validación | Completa (esquema simple: columnas requeridas + reglas del handler) | `IniciarImportacionCommandValidator`, `IImportRowHandler.ColumnasRequeridas` |
| Lotes | Completa | `ImportBatchProcessorJob`/`ExportBatchProcessorJob`, `TamanoLoteFilas` |
| Progreso | Completa | `ObtenerImportJobQuery`/`ObtenerExportJobQuery` |
| Errores | Completa (aislamiento por ítem en dos niveles, desde el diseño inicial) | `ImportJobError`/`ExportJobError`, tests de aislamiento |
| Reanudación | Completa para importación; limitación honesta documentada para exportación (posible duplicado de filas en el archivo tras una caída a mitad de chunk) | `Importacion_ReanudadaEnUnaInstanciaDeJobDistinta...`, sección "Reanudación" |

### Pendientes explícitos

- **Reanudación de exportación sin riesgo de filas duplicadas** — requiere checkpoint de offset de byte
  dentro del archivo de resultado, ver "Reanudación".
- **Archivos de más de 10 MB / millones de filas** — ver "Límites de tamaño de archivo".
- **Antivirus sobre el archivo importado** — este módulo no reutiliza `IAntivirusScanner` de Documents (ver
  "Relación con Documents"); un consumidor real que reciba archivos de usuarios externos no confiables
  debería agregarlo antes de `IniciarImportacionCommandHandler`.
- **Cancelación manual de un `ImportJob`/`ExportJob` en curso** — no hay comando para eso hoy; ver "Sin
  `IHasConcurrencyToken`" para la implicación de agregarlo en el futuro.
- **Registro de los jobs en un host productivo con Quartz HA real** — el host de referencia no los
  registra (mismo estado que el resto de los jobs de Fase 6).
- **Formatos de archivo alternativos (JSON, Excel)** — solo CSV en este corte, ver "Por qué CSV".
- **Publicación/consumo contra un broker Kafka real de punta a punta** — mismo estado que el resto de
  Fase 6 (`docs/catalogo-eventos.md`).

## Pruebas

`ImportExportEndpointsIntegrationTests` (SQL Server real + filesystem real, Testcontainers) cubre: 401 sin
autenticación, alta con tipo no registrado (404), progreso avanzando en 3 ciclos visibles para 7 filas con
`TamanoLoteFilas=3` (verificado que `FilasProcesadas` avanza 3 → 6 → 7, nunca salta directo a completo),
reanudación con una instancia de job completamente nueva sin reprocesar filas ya confirmadas, aislamiento
de 2 filas inválidas de 5 (errores con su número de fila correcto, resto del lote aplicado), aislamiento
multi-tenant (dos tenants importando el mismo tipo nunca mezclan datos), camino completo de exportación
(siembra vía importación, exporta, descarga, verifica contenido CSV exacto), un archivo mayor a 10 MB
rechazado (400) y un archivo con contenido no-UTF8 válido rechazado (400, corrección del bug real
encontrado por esta misma cobertura), y descarga rechazada (409)
mientras el job de exportación no finalizó. 10/10 pasan.

## Referencias

- [`plan-maestro-bitcode-ia.md`](plan-maestro-bitcode-ia.md) — Fase 6, fila "Import and Export".
- [`convenciones.md`](convenciones.md) — reglas duras 1, 2, 5, 6, 8, 27, 28.
- [`guia-integration-hub.md`](guia-integration-hub.md) — mismo patrón de aislamiento por ítem desde el
  diseño inicial, Quartz HA, `IgnoreQueryFilters` + `SaveChangesAsync` explícito en un job cross-tenant.
- [`guia-documents.md`](guia-documents.md) — `IDocumentBlobStore`/`FileSystemDocumentBlobStore`, referencia
  de diseño para `IImportExportFileStore` (ver "Relación con Documents" para por qué NO se reutiliza
  directamente).
- `src/Platform/BitCode.Platform.ImportExport/` — implementación.
- `samples/Sample.ImportExport.Api/` — host de referencia, incluye `Clientes/` con la implementación de
  referencia de `IImportRowHandler`/`IExportDataSource`.
- `samples/Sample.ImportExport.Api.Tests/Integration/ImportExportEndpointsIntegrationTests.cs` — evidencia
  de los criterios de aceptación.
