# Documents — Fase 6, módulo 5

**Tarea:** Fase 6 — Plataforma funcional empresarial, módulo 5 (Documents) del
[Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Dependencias declaradas:** "Security y Storage". Security = Fase 2 (cerrada). El repositorio NO tiene
hoy un módulo `Shared.Infrastructure.Storage` genérico (verificado con `Glob src/Shared.Infrastructure.*`
antes de esta tarea) -- "Storage" se interpretó como "un mecanismo de almacenamiento de blobs
desacoplado" propio de este módulo (`IDocumentBlobStore`), ver sección "Almacenamiento" más abajo.
**Fecha:** 2026-09-09.

Módulo de plataforma que provee metadata y clasificación de documentos (`Documento`), versionado
inmutable con hash de contenido (`DocumentoVersion`), carga y descarga segura contra un almacenamiento de
blobs desacoplado, escaneo antivirus modelado como estado explícito de la versión, retención y
disposición, y autorización por recurso (RBAC + ABAC de alcance por `documentoId`) con auditoría real de
las operaciones críticas. Mismo criterio que Organization/Catalogs and Parameters/Feature Management
(Fase 6, módulos 2/3/4): `Documento`/`DocumentoVersion` son `AggregateRoot<Guid>` **propios** de este
módulo, no reutilizan tipos ajenos de otro bounded context.

## Ubicación

- Librería: `src/Platform/BitCode.Platform.Documents/` (namespace `BitCode.Framework.Platform.Documents`,
  ver `docs/convenciones.md`, sección "Namespaces de módulos de Platform").
- Aplicación de referencia: `samples/Sample.Documents.Api/`.
- Pruebas de integración (SQL Server real vía Testcontainers + filesystem real): `samples/Sample.Documents.Api.Tests/`.

## Capacidades

| Capacidad (Épica de Documents) | Alcance de este primer corte |
|---|---|
| Metadata y clasificación | `Documento`: `Titulo`, `Descripcion`, `Clasificacion` (string libre, ver "Pendientes" #1). |
| Versionado | `DocumentoVersion` inmutable, numeración incremental por documento (único por `(DocumentoId, Numero)`); subir una versión nueva nunca borra la anterior. |
| Hash de contenido | SHA-256 calculado en el borde de aplicación al recibir el archivo, persistido en `DocumentoVersion.HashSha256`. |
| Carga y descarga segura | Multipart (`IFormFile`) materializado a `byte[]` en el endpoint; descarga como adjunto (`Content-Disposition`), nunca inline. |
| Escaneo antivirus | Estado explícito (`EstadoEscaneo`: `PendienteEscaneo`/`Limpio`/`Infectado`) + `IAntivirusScanner` (implementación de referencia que reconoce el string de prueba EICAR). Una versión `Infectada` NUNCA se descarga. |
| Retención y disposición | `Documento.RetencionDias` + `DisponibleParaDisposicionDesde` (calculada) + guardrail que impide disponer antes de tiempo. Sin job automático (ver "Pendientes" #2). |
| Autorización por recurso | RBAC (`DocumentsPermissions`) + ABAC de alcance por `documentoId` (`AttributeScopeAbacRule`, F2-08) sobre subir versión/descargar/disponer. |
| Auditoría | Toda mutación y la DESCARGA (aunque sea de solo lectura) escriben `AuditEntry` vía `IAuditWriter` (F2-15). |
| Integración con almacenamiento desacoplado | `IDocumentBlobStore`, implementación de referencia sobre filesystem local (`FileSystemDocumentBlobStore`). |

## Modelo de datos

```
Documento (AggregateRoot<Guid>)
  Titulo, Descripcion, Clasificacion, RetencionDias
  VersionActualId, VersionActualNumero
  DisponibleParaDisposicionDesde => CreatedAtUtc.AddDays(RetencionDias)  -- calculada, no persistida

DocumentoVersion (AggregateRoot<Guid>, único por (DocumentoId, Numero))
  DocumentoId, Numero, NombreArchivo, ContentType, TamanioBytes
  HashSha256, BlobKey, EstadoEscaneo, EscaneadaAtUtc
```

Un `Documento` tiene una o más `DocumentoVersion`; `VersionActualId`/`VersionActualNumero` apuntan siempre
a la más reciente, pero las anteriores siguen existiendo y son descargables por número explícito
(`GET /api/v1/documentos/{id}/descargar?numero=1`).

## Almacenamiento (`IDocumentBlobStore`)

Abstracción propia de este módulo (no hay un `Shared.Infrastructure.Storage` genérico en el repositorio
todavía). Contrato mínimo: `UploadAsync`/`DownloadAsync`/`DeleteAsync`/`ExistsAsync` sobre un `Stream` y
una clave opaca (`blobKey`, generada por `BlobKeyFactory` a partir de GUIDs propios, nunca de un valor
provisto por el cliente HTTP). Implementación de referencia registrada por defecto:
`FileSystemDocumentBlobStore` (filesystem local, configurable vía `Documents:BlobStore:RootPath`), con
escritura atómica (archivo temporal + `File.Move`) para que una descarga concurrente con una subida en
curso nunca vea contenido parcial.

**Orden de escritura deliberado** (`CrearDocumentoCommandHandler`/`SubirVersionDocumentoCommandHandler`):
el contenido se sube al blob store ANTES de persistir la metadata en `DocumentsDbContext`. Si la subida al
blob store falla, el comando falla completo sin dejar ningún registro de `Documento`/`DocumentoVersion`
huérfano (metadata apuntando a un archivo que nunca se escribió). El orden inverso podría, en el caso raro
de que el blob store fallara después del commit de la transacción de base de datos, dejar exactamente ese
estado inconsistente -- ver la prueba de recuperación en la sección "Pruebas".

Un adapter real de Azure Blob Storage/S3/MinIO es un **pendiente explícito** (ver "Pendientes" #3): eso es
una decisión de infraestructura de producción que excede el alcance de este corte.

## Escaneo antivirus (`IAntivirusScanner`)

Ningún motor antivirus real (ClamAV, Windows Defender, un servicio cloud) está integrado en este primer
corte -- eso es infraestructura externa. La implementación de referencia registrada por defecto
(`ReferenceAntivirusScanner`) reconoce el
["EICAR Standard Anti-Virus Test File"](https://en.wikipedia.org/wiki/EICAR_test_file) (el string de
prueba estándar de facto de la industria antivirus desde 1990, sin ser código malicioso real) -- suficiente
para ejercitar de punta a punta, con evidencia real, el flujo de negocio completo: subir contenido con esa
firma → la versión queda `Infectada` → cualquier intento de descarga (aunque el actor tenga RBAC + ABAC)
se rechaza con 409. Un consumidor real reemplaza el registro de `IAntivirusScanner` (`services.AddSingleton<IAntivirusScanner, MiScannerReal>()`
DESPUÉS de `AddSharedDocuments`) sin tocar ningún handler de este módulo.

## Retención y disposición

`Documento.RetencionDias` se fija al crear el documento; `DisponibleParaDisposicionDesde` es una
propiedad CALCULADA (`CreatedAtUtc.AddDays(RetencionDias)`), no un job de limpieza automática -- mismo
criterio de simplificación deliberada que `Retention-Cleanup.sql` (F5-07): ese script también declara la
política de retención sin ejecutar el borrado por su cuenta. `DELETE /api/v1/documentos/{id}` (permiso
`documents.documentos.disponer`) dispara la disposición real (baja lógica, `ISoftDelete`, aplicada por
`SoftDeleteInterceptor` cuando el handler llama `IRepository.Remove`) -- rechaza con 409 (`Result.Failure`,
nunca una excepción) un intento anterior a `DisponibleParaDisposicionDesde`. Un job externo que dispare
esta disposición automáticamente al cumplirse la retención es un pendiente explícito (ver "Pendientes" #2).

## Cómo consumirlo desde un host

```csharp
// InfrastructureModule.cs
services.AddHttpContextTenantProvider();
services.AddSharedPersistence<DocumentsDbContext>(connectionString);
services.AddSharedSecurity<ApplicationUser, ApplicationRole, TuIdentityDbContext>(configuration);
services.AddSharedAbacAuthorization(options =>
{
    // Opcional: restringe qué documentos puede versionar/descargar/disponer un actor cuyo rol solo
    // administra un subconjunto de documentos.
    options.ScopeRules.Add(new AbacScopeAttributeRule
    {
        ResourceType = "documents.documentos",
        ResourceAttributeKey = "documentoId",
        ClaimType = "documento_id",
    });
});
services.AddSharedAuditing();
services.AddSharedDocuments(options => configuration.GetSection("Documents:BlobStore").Bind(options));
services.AddHttpContextIdempotencyKeyProvider();
services.AddSharedApplication(typeof(InfrastructureModule).Assembly, typeof(DocumentsDbContext).Assembly);

// TuApiModule.cs
[DependsOn(typeof(InfrastructureModule))]
public class TuApiModule : IWebFrameworkModule
{
    public void ConfigureApplication(WebApplication app) => app.MapDocumentsEndpoints();
}
```

Ver `samples/Sample.Documents.Api/` para el host de referencia completo.

## Endpoints (`/api/v1/documentos/...`)

| Método | Ruta | Permiso | Notas |
|---|---|---|---|
| POST | `/` | `documents.documentos.crear` | Multipart (`archivo`, `titulo`, `descripcion?`, `clasificacion`, `retencionDias`). Idempotente (F1-22). Crea el documento + versión 1. |
| GET | `/{id}` | `documents.documentos.ver` | |
| GET | `/` | `documents.documentos.ver` | Paginado. |
| GET | `/{id}/versiones` | `documents.documentos.ver` | Lista TODAS las versiones (incluidas las no vigentes). |
| POST | `/{id}/versiones` | `documents.documentos.subirversion` | Multipart (`archivo`). RBAC + ABAC (alcance por `documentoId`). 404 si el documento no existe. |
| GET | `/{id}/descargar?numero=` | `documents.documentos.descargar` | `numero` nulo descarga la versión vigente. RBAC + ABAC (alcance por `documentoId`). 409 si la versión no está `Limpia` (pendiente o infectada). |
| DELETE | `/{id}` | `documents.documentos.disponer` | RBAC + ABAC (alcance por `documentoId`). 409 si la retención todavía no venció. |

## RBAC + ABAC por documento (requisito común de Fase 6)

Mismo mecanismo, deliberadamente, que `FeatureManagementPermissions.FlagsActivar`/`OrganizationPermissions.EmpresasDesactivar`:
además del permiso RBAC correspondiente, `SubirVersionDocumentoCommandHandler`/
`DescargarDocumentoVersionQueryHandler`/`DisponerDocumentoCommandHandler` evalúan explícitamente
`IAuthorizationPolicyEvaluator` (F2-08) con la regla ABAC incorporada del framework
(`AttributeScopeAbacRule`) sobre el atributo `documentoId`. Se decidió reutilizar este mecanismo genérico
en vez de introducir un campo `PropietarioId`/lista de actores permitidos propio de `Documento` porque es
exactamente el mismo problema ("¿este actor puede operar ESTA instancia concreta del recurso?") que
Feature Management/Organization ya resolvieron con el mismo mecanismo -- evita duplicar en cada módulo de
plataforma una tabla de permisos por recurso que el framework ya provee de forma transversal. El host de
referencia (`Sample.Documents.Api`) NO registra ninguna regla de alcance por defecto (lista vacía = sin
restricción adicional más allá de RBAC) -- el test de integración la configura explícitamente para
demostrar el criterio de aceptación de punta a punta (ver "Pruebas").

## Auditoría

Toda mutación (`crear`/`subirversion`/`disponer`) y la DESCARGA (aunque sea de solo lectura) escriben una
`AuditEntry` vía `IAuditWriter` (F2-15), con el actor resuelto de `IDocumentsActorContext` (usuario
autenticado del JWT, o `"system"` fuera de un pipeline HTTP). Las decisiones ABAC denegadas y los intentos
de descargar una versión no `Limpia` también quedan auditadas (`AuditOutcome.Denied`), y una falla de
recuperación del blob store queda auditada como `AuditOutcome.Error` -- ver "Descarga es un `IQuery` que
audita" más abajo.

## Descarga es un `IQuery` que audita (nota de diseño, regla dura 2)

`DescargarDocumentoVersionQuery` es deliberadamente un `IQuery<TResponse>` (nunca modifica ninguna entidad
de `DocumentsDbContext`) aunque evalúa ABAC y escribe una entrada de auditoría. Esto NO viola la regla dura
2 de `docs/convenciones.md` ("un `IQuery` nunca modifica datos"): esa regla protege contra queries que
mutan entidades de EF sin la protección transaccional de `TransactionBehavior`/`IUnitOfWork`. `IAuditWriter`
(F2-15) es un sumidero independiente (`InMemoryAuditWriter` por defecto, o el backend real que registre un
consumidor) que nunca pasa por `DocumentsDbContext` -- escribir ahí desde un handler de query no deja
ningún cambio de negocio a medias sin que el framework lo detecte.

## Eventos de dominio e integración

`Documento.RegistrarVersionInicial`/`RegistrarNuevaVersion` y `DocumentoVersion.MarcarEscaneada` levantan
`DocumentoSubidoIntegrationEvent`/`DocumentoVersionCreadaIntegrationEvent`/`DocumentoEscaneadoIntegrationEvent`
vía `RaiseDomainEvent` -- `OutboxSaveChangesInterceptor` (F1-23) los persiste atómicamente junto con el
cambio de negocio en `DocumentsDbContext`. Registrados en `docs/catalogo-eventos.md` (regla dura 27).
`samples/Sample.Documents.Api` no registra `AddSharedKafkaEventing` -- los eventos quedan en
`OutboxMessage` sin relay activo (mismo estado que Organization/Catalogs and Parameters/Feature
Management; el mecanismo de publicación en sí ya está probado de punta a punta en `Sample.Eventing`,
F3-13).

## Pendientes explícitos

1. **Clasificación como string libre, no un catálogo cerrado.** `Documento.Clasificacion` no valida contra
   ningún vocabulario controlado en este primer corte -- una integración real con Catalogs and Parameters
   (Fase 6, módulo 3, por ejemplo un catálogo `TIPOS_DOCUMENTO`) es una extensión natural, no implementada
   por decisión explícita de alcance.
2. **Sin job de limpieza automática de retención.** Ver sección "Retención y disposición" arriba -- el
   cálculo y el guardrail están completos y probados; el disparo automático (análogo a un futuro job
   basado en el mismo criterio que `Retention-Cleanup.sql`, F5-07) queda como trabajo futuro.
3. **Sin adapter real de object storage en la nube.** `IDocumentBlobStore` solo tiene la implementación de
   referencia sobre filesystem local (`FileSystemDocumentBlobStore`) -- un adapter de Azure Blob
   Storage/S3/MinIO es una decisión de infraestructura de producción (endpoint, credenciales, cifrado en
   tránsito/reposo) que excede el alcance de este corte (Plan Maestro, sección 3.2: implementarlo hoy sin
   un consumidor productivo real sería sobre-ingeniería).
4. **Sin motor antivirus real.** Ver sección "Escaneo antivirus" arriba.
5. **Sin verificación activa de integridad en la descarga.** `HashSha256` se calcula y persiste al subir,
   pero `DescargarDocumentoVersionQueryHandler` no recalcula el hash del contenido leído del blob store
   para compararlo antes de servirlo -- el campo existe y sirve para detectar duplicados/como referencia
   externa, la verificación activa en el camino de lectura es una extensión natural no implementada en
   este corte.
6. **Condición de carrera real en la numeración de versiones bajo concurrencia extrema.** Mismo hallazgo
   documentado en Feature Management/Catalogs and Parameters para el mismo tipo de check-then-act:
   `SubirVersionDocumentoCommandHandler` calcula `Numero = documento.VersionActualNumero + 1` (lectura) y
   recién después inserta (escritura), sin `ITransactionalCommand` ni aislamiento serializable. El índice
   único `(DocumentoId, Numero)` en `DocumentsDbContext` es el guardrail de datos que evita persistir dos
   versiones con el mismo número bajo una carrera real, pero la carrera perdedora recibe una
   `DbUpdateException` no traducida a `Result.Failure`/409 en este primer corte (el
   `ExceptionHandlerMiddleware` genérico la convierte en un 500).
7. **Sin `dotnet ef migrations` real.** Mismo estado que el resto del repositorio (`EnsureCreatedAsync` en
   `Program.cs` de `Sample.Documents.Api`) -- no es deuda nueva de este módulo.
8. **Sin publicación real contra un broker Kafka productivo.** Ver sección "Eventos de dominio e
   integración" arriba.
9. **Host de referencia con dos bases de datos separadas.** Mismo patrón que
   `Sample.FeatureManagement.Api`/`Sample.Catalogs.Api` -- `DocumentsDbContext`
   (`ConnectionStrings:Default`) y un `SampleIdentityDbContext` propio (`ConnectionStrings:Identity`) solo
   para poder emitir JWTs reales en las pruebas de integración.

## Pruebas

`samples/Sample.Documents.Api.Tests/Integration/DocumentsEndpointsIntegrationTests.cs` -- contra SQL
Server real (Testcontainers, `SqlServerContainerFixture`) y un filesystem real (nunca un mock de
`IDocumentBlobStore`), vía `WebApplicationFactory<Program>`:

- Alta de documento sin autenticación → 401.
- Alta de documento con permiso → 201 + idempotencia (misma `Idempotency-Key` + mismo cuerpo → mismo Id) +
  hash SHA-256 correcto + escaneo `Limpio`.
- Descarga de la versión vigente devuelve el contenido original byte a byte.
- **Prueba de seguridad #1** (Gate de salida de Fase 6): descarga sin el permiso RBAC → 403.
- **Prueba de seguridad #2**: descarga fuera del alcance ABAC configurado (`documento_id`) → 403, aunque
  el actor tenga el permiso RBAC `documents.documentos.descargar` y sí pueda descargar su propio documento.
- **Prueba de seguridad #3** (criterio de aceptación central de "Escaneo antivirus"): subir contenido con
  la firma EICAR → la versión queda `Infectada` → la descarga se rechaza con 409, aunque el actor tenga
  RBAC + ABAC completos.
- Versionado: subir una versión nueva incrementa el número vigente y preserva el contenido de la versión
  anterior, descargable por número explícito.
- Retención: disponer antes de vencer la retención → 409; con `RetencionDias = 0`, disponer inmediatamente
  → 204, y el documento deja de ser visible (baja lógica real, filtro global de `ISoftDelete`).
- **Prueba de RECUPERACIÓN** (Gate de salida de Fase 6): metadata presente en `DocumentsDbContext`, archivo
  físico borrado directamente del filesystem real de prueba (simulando corrupción/pérdida del
  almacenamiento subyacente) → la descarga degrada a un error controlado (500 con `ProblemDetails`
  estructurado, `AuditOutcome.Error` auditado), nunca una excepción sin manejar ni una respuesta rota.

Comando de verificación real (evidencia de ejecución en el reporte de cierre de esta tarea):

```powershell
dotnet test samples/Sample.Documents.Api.Tests/Sample.Documents.Api.Tests.csproj
```

Resultado real de la última ejecución: 10 pruebas, 10 correctas, 0 fallidas (contra SQL Server real vía
Testcontainers y un filesystem real).
