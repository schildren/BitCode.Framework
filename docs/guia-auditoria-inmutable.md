# Guía — Auditoría inmutable: `IAuditWriter`, cadena de integridad, firma de lotes, exportación WORM y redacción de PII (F2-15/F2-16/F2-17/F2-18/F2-19)

**Tareas:** F2-15, F2-16, F2-17, F2-18 y F2-19 (Fase 2, Épica F2-D — Auditoría inmutable) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Entregables:** esquema append-only (F2-15); servicio de integridad (F2-16); mecanismo aprobado de firma y timestamp (F2-17); pipeline de retención WORM (F2-18); política y filtros de redacción de PII (F2-19).
**Criterios de aceptación:** "Campos críticos completos" (F2-15); "Manipulación detectable" (F2-16); "Verificación independiente" (F2-17); "Escritura y lectura probadas" (F2-18); "Logs sin PII no autorizada" (F2-19).

F2-15 es la base de datos/modelo de la Épica F2-D. F2-16 (cadena de integridad), F2-17 (firma de lotes),
F2-18 (exportación WORM) y F2-19 (redacción de PII, esta guía las cubre las cuatro últimas) son piezas
ADICIONALES que se apoyan en ese esquema sin haber requerido ningún cambio de contrato público breaking
(`AuditEntry` ya reservaba `PreviousAuditHash` desde F2-15). La tarea siguiente de la épica sigue pendiente:

- **F2-20** (consulta de auditoría): una API de lectura sobre el almacenamiento real detrás de
  `IAuditWriter` (ni F2-15 ni F2-16 incluyen ningún mecanismo de lectura más allá de
  `InMemoryAuditWriter.Entries`, pensado solo para inspección en pruebas/desarrollo local -- F2-18 sí agrega
  lectura, pero acotada a lo que el propio proyecto exportó a WORM, no una API administrativa general).

## Qué resuelve esta tarea

Un registro de auditoría (`AuditEntry`, `Shared.Infrastructure.Security.Audit`) captura, de forma
inmutable, quién hizo qué sobre qué recurso, con qué resultado y en qué tenant — los campos críticos que
el criterio de aceptación exige completos:

| Campo | Qué captura |
|---|---|
| `Id` | Identificador único, generado siempre por el writer (nunca por el llamador). |
| `OccurredAtUtc` | Timestamp UTC de la escritura. |
| `Actor` (`AuditActor`: `Id` + `AuditActorType`) | Quién ejecutó la operación — usuario, identidad de servicio (F2-04) o proceso interno del sistema. |
| `TenantId` | Tenant/empresa de la operación (mismo concepto que `ITenantContext`, F1-15). |
| `Action` | La acción ejecutada, misma convención `"{entidad}.{accion}"` que un permiso RBAC (F2-07). |
| `Resource` (`AuditResource`: `Type` + `Id?`) | El recurso de negocio afectado. |
| `Outcome` (`AuditOutcome`: `Success`/`Denied`/`Error`) | Resultado — distingue una denegación de autorización esperada (RBAC/ABAC) de un fallo técnico. |
| `Reason` | Motivo, típicamente el código de denegación/error. |
| `CorrelationId`/`TraceId`/`IpAddress` | Trazas para correlacionar con logs/telemetría del mismo request. |
| `Metadata` | Contexto adicional específico del caso de uso (valores de texto simple). |
| `AuditHash` | SHA-256 sobre todos los campos críticos de arriba (`AuditHashCalculator`) — determinístico para el mismo contenido, cambia ante cualquier alteración de cualquiera de esos campos. |
| `PreviousAuditHash` | Hash del registro anterior de la misma cadena (F2-16); `null` para el registro génesis de una cadena. |

## Por qué "append-only" no depende solo de la base de datos

`AuditEntry` no tiene ningún setter público (todas sus propiedades son de solo lectura) e `IAuditWriter`
expone un único método, `WriteAsync` — no existe ningún camino de código, ni en este tipo ni en la
interfaz, para actualizar o eliminar un registro ya escrito. Esto es intencional: una restricción de
permisos de esquema o un trigger en la base de datos es una defensa adicional válida (y necesaria para el
destino WORM real de F2-18), pero no reemplaza que el propio contrato de aplicación no ofrezca la
operación en primer lugar.

```csharp
public interface IAuditWriter
{
    Task<Result<AuditEntry>> WriteAsync(AuditEntryRequest request, CancellationToken cancellationToken = default);
}
```

## Registro: `AddSharedAuditing`

```csharp
services.AddSharedAuditing();
```

Registra `InMemoryAuditWriter` como implementación por defecto de `IAuditWriter` (`TryAddSingleton`) — un
placeholder en memoria del propio proceso, sin persistencia entre reinicios ni entre instancias, pensado
para desarrollo local y para que la interfaz quede lista y probada. **No es una fuente de verdad
productiva de auditoría**: un proyecto consumidor que necesite auditoría persistente real (tabla SQL
append-only, event store, o el destino WORM de F2-18) registra su propia implementación de `IAuditWriter`
DESPUÉS de llamar a `AddSharedAuditing` — el último registro para el mismo tipo de servicio gana la
resolución (mismo principio que `AddSharedPermissionEvaluation`, F2-07), sin necesitar `Replace` explícito.

## Cadena de integridad (F2-16)

Cada `AuditEntry.PreviousAuditHash` enlaza el registro con el `AuditHash` del registro inmediatamente
anterior de su misma cadena. Este enlace, sumado al propio `AuditHash` de cada registro (que ya cubre todos
sus campos críticos, F2-15), es lo que permite detectar manipulación:

- **Modificar un registro ya escrito** (cambiar cualquier campo directamente en el almacenamiento
  subyacente, sin pasar por `IAuditWriter`) invalida el `AuditHash` de ese registro, porque `AuditHash` fue
  calculado sobre el contenido original.
- **Eliminar un registro completo de la cadena, o reordenar la secuencia**, no cambia ningún `AuditHash`
  individual, pero rompe el enlace: el `PreviousAuditHash` del registro siguiente ya no coincide con el
  `AuditHash` del registro que ahora queda inmediatamente antes en la secuencia.

### Alcance de "misma cadena": por tenant

`IAuditWriter` mantiene **una cadena de integridad independiente por cada `TenantId`** (incluida una cadena
propia para `TenantId == null`, operaciones de plataforma sin tenant) — no una única cadena global del
proceso. Esta es una decisión deliberada de F2-16, justificada así:

- La auditoría de tenants distintos ya es lógicamente independiente entre sí — ningún caso de uso necesita
  verificar la integridad conjunta de auditoría de dos tenants sin relación entre sí en una sola cadena.
- Una cadena única global mezclaría en un mismo enlace el orden de escritura de operaciones de tenants
  completamente independientes, lo que no aporta ninguna garantía adicional real y sí dificulta tanto la
  verificación (`IAuditIntegrityVerifier.Verify` espera ya una secuencia de una sola cadena) como la futura
  exportación WORM (F2-18), que naturalmente se hará por tenant.
- Evita además una contención de escritura innecesaria entre tenants sin relación: con una cadena por
  tenant, dos escrituras concurrentes de tenants distintos nunca compiten por el mismo "último hash".

`InMemoryAuditWriter` implementa esto con un `ConcurrentDictionary` interno que guarda el último `AuditHash`
escrito por cadena — la clave interna no es `Guid?` directamente (`ConcurrentDictionary` no admite en la
práctica una clave `null` real, incluso con `TKey = Guid?`: un `Nullable<Guid>` sin valor se boxea a una
referencia nula real y el chequeo interno de "clave no nula" lo rechaza) sino una `struct` privada
(`ChainKey`) que representa "sin tenant" sin usar `null`.

Leer el último hash de una cadena para determinar el `PreviousAuditHash` de una entrada nueva y hacer
visible esa misma entrada en `Entries` (`Enqueue`) no pueden ser dos pasos independientes: el orden de
aparición en `Entries` tiene que coincidir siempre con el orden lógico de ese enlace, o
`IAuditIntegrityVerifier.Verify` reporta un falso `PreviousHashLinkMismatch` sobre escrituras
completamente legítimas bajo concurrencia (dos escrituras del mismo tenant podrían, si esos dos pasos no
fueran atómicos entre sí, fijar el enlace en un orden relativo y encolar en el orden relativo contrario).
Por eso `InMemoryAuditWriter` serializa, con un `lock` por cadena (una cadena, un objeto de lock — cadenas
de tenants distintos nunca se bloquean entre sí), la sección "leer último hash → construir la entrada →
encolarla" para cada cadena.

### Servicio de integridad: `IAuditIntegrityVerifier`

```csharp
public interface IAuditIntegrityVerifier
{
    AuditIntegrityVerificationResult Verify(IReadOnlyList<AuditEntry> chain);
}
```

`Verify` recibe una secuencia de `AuditEntry` **de una única cadena** (mismo `TenantId`), **ya ordenada**
cronológicamente (mismo orden en que `IAuditWriter` las escribió) y recorre la secuencia comparando, para
cada registro: (a) su `AuditHash` recalculado (`AuditHashCalculator.Compute` sobre sus campos actuales)
contra el `AuditHash` almacenado, y (b) su `PreviousAuditHash` contra el `AuditHash` del registro anterior
en la secuencia (o contra `null` para el primer registro). Es una operación puramente de cómputo — no lee
de ningún almacenamiento por sí misma, no depende de `IAuditWriter`, y por lo tanto funciona igual sobre
`InMemoryAuditWriter.Entries` que sobre lo que en el futuro devuelva la API de lectura de F2-20.

El resultado (`AuditIntegrityVerificationResult`) no es un simple booleano — expone `IsValid`, y cuando es
`false`, además `BrokenAtIndex` (posición en la secuencia provista), `BrokenEntryId` (`AuditEntry.Id` del
registro donde se detectó la ruptura) y `Reason` (`AuditIntegrityBreakReason.HashMismatch` o
`.PreviousHashLinkMismatch`) — "manipulación detectable" (criterio de aceptación de F2-16) exige poder
diagnosticar la manipulación, no solo saber que ocurrió.

```csharp
var result = auditIntegrityVerifier.Verify(entriesDeUnaSolaCadena);
if (!result.IsValid)
{
    logger.LogCritical(
        "Cadena de auditoría comprometida en la posición {Index} (registro {EntryId}): {Reason}",
        result.BrokenAtIndex, result.BrokenEntryId, result.Reason);
}
```

`AddSharedAuditing` registra `AuditIntegrityVerifier` como implementación por defecto de
`IAuditIntegrityVerifier` (`TryAddSingleton`) — sin estado propio, no depende de qué implementación de
`IAuditWriter` esté registrada.

### Qué NO resuelve F2-16

- **No persiste nada nuevo**: `InMemoryAuditWriter` sigue siendo en memoria del propio proceso; F2-16 solo
  agrega el cálculo del enlace y el servicio de verificación, no ningún almacenamiento. Un proyecto que
  conecte su propia implementación de `IAuditWriter` (tabla SQL, event store) es responsable de calcular y
  persistir `PreviousAuditHash` con el mismo criterio (por tenant) si quiere que `IAuditIntegrityVerifier`
  funcione sobre sus datos.
- **No firma criptográficamente los registros por sí sola** — la firma/timestamp de lotes o eventos ya
  escritos es F2-17 (sección siguiente); la cadena de integridad de F2-16 detecta manipulación posterior al
  hecho, pero no prueba frente a terceros que la cadena no fue reconstruida íntegramente desde cero por
  quien tiene acceso de escritura al almacenamiento subyacente (eso requiere una firma con una clave que ese
  mismo actor no controle, o el destino WORM de F2-18).
- **No exporta a almacenamiento inmutable/WORM** — es F2-18.
- **No expone ninguna API de lectura/consulta administrativa** — sigue siendo F2-20;
  `InMemoryAuditWriter.Entries` no es esa API, solo inspección de desarrollo/pruebas.
- **No agrupa por tenant automáticamente** — `IAuditIntegrityVerifier.Verify` espera que el llamador ya le
  entregue la secuencia de una sola cadena; agrupar `InMemoryAuditWriter.Entries` (o el resultado de F2-20)
  por `TenantId` antes de verificar es responsabilidad del llamador.

## Firma de lotes: `IAuditBatchSigner` (F2-17)

### Qué hueco cierra, exactamente

`IAuditIntegrityVerifier` (F2-16) recalcula, con `AuditHashCalculator` — un algoritmo **público**, el mismo
que usa cualquiera que escriba con `IAuditWriter` —, los mismos hashes que ya están almacenados. Esto detecta
que un registro fue alterado sin recalcular su `AuditHash`, o que la secuencia fue recortada/reordenada. Pero,
por diseño, **no** detecta que un atacante con acceso de **escritura** al almacenamiento subyacente reconstruya
una cadena alternativa completa: cambiar un campo de un registro y volver a calcular, con el mismo
`AuditHashCalculator`, tanto el `AuditHash` de ese registro como el `PreviousAuditHash` de todos los
siguientes produce una cadena internamente consistente que `IAuditIntegrityVerifier.Verify` no distingue de
la original.

`IAuditBatchSigner` cierra ese hueco exigiendo una clave que esa reconstrucción no puede reproducir:

```csharp
public interface IAuditBatchSigner
{
    Task<Result<AuditBatchSignature>> SignAsync(
        IReadOnlyList<AuditEntry> batch, CancellationToken cancellationToken = default);

    Task<Result<bool>> VerifyAsync(
        IReadOnlyList<AuditEntry> batch, AuditBatchSignature signature, CancellationToken cancellationToken = default);
}
```

### Mecanismo elegido: HMAC-SHA256 sobre `ISecretProvider` (F2-12), reutilizando el patrón de F2-13

`HmacAuditBatchSigner` (implementación por defecto) firma con **HMAC-SHA256**, resolviendo el material de
clave vía `ISecretProvider` (F2-12) con clave lógica versionada — el mismo patrón exacto de
`AesGcmEncryptionProvider` (F2-13, `docs/politica-criptografica.md`): `AuditBatchSigningOptions` expone
`ActiveKeyVersionSecretKey` (versión activa para firmar lotes nuevos) y `KeyMaterialSecretKeyPrefix` (prefijo
para resolver el material, mínimo 32 bytes, de cada versión). La versión usada va embebida en
`AuditBatchSignature.KeyVersion`, así que rotar la clave activa no invalida firmas ya emitidas — la
verificación resuelve la versión que corresponde a la firma, no la versión activa vigente.

**Por qué HMAC (simétrico) y no una firma asimétrica**: el framework hoy no expone ninguna abstracción de
par de claves asimétrico/PKI — solo `ISecretProvider` (material simétrico/secretos) y `IEncryptionProvider`
(AES-256-GCM, también simétrico). Agregar una gestión de claves asimétricas nueva habría sido introducir una
pieza de infraestructura criptográfica que el entregable de F2-17 ("Integrar firma de lotes o eventos",
"Mecanismo aprobado") no pide explícitamente, y por lo tanto fuera del alcance mínimo de la tarea — F2-17
reutiliza deliberadamente F2-12/F2-13 en lugar de crear un sistema de claves paralelo. HMAC-SHA256 es un
algoritmo aprobado por `docs/politica-criptografica.md` (autenticado, sin modo de bloque inseguro) y es
suficiente para cerrar el hueco documentado: un atacante con acceso de **escritura** al almacenamiento de
auditoría, pero **sin** acceso al proveedor de secretos donde vive la clave de firma, no puede producir una
firma válida sobre una cadena alternativa, por más consistente que la reconstruya.

**"Verificación independiente" con un esquema simétrico**: el criterio de aceptación no exige que la
verificación no requiera ningún secreto (eso sería exclusivo de una firma asimétrica con clave pública) —
exige que sea independiente de quien firmó. Con HMAC esto se sostiene cuando firmante y verificador son
procesos/servicios distintos que comparten el mismo perímetro de confianza (el proveedor de secretos), pero
ninguno de los dos tiene, además, acceso de escritura directa al almacenamiento de auditoría — por ejemplo,
un servicio que firma lotes al escribirlos y un proceso de verificación periódico/de cumplimiento que solo
lee del almacenamiento y del proveedor de secretos. Si un proyecto consumidor necesitara verificación por un
tercero SIN acceso al proveedor de secretos (ej. un auditor externo), necesita una firma asimétrica — fuera
de alcance de esta tarea (ver "Qué NO resuelve F2-17").

### Qué cubre la firma: recalculado, no el campo `AuditHash` almacenado

`HmacAuditBatchSigner` firma, para cada registro del lote en el orden dado: `Id`, el hash de contenido
**recalculado** con `AuditHashCalculator.Compute` sobre los campos actuales del registro (no el valor ya
almacenado en `AuditEntry.AuditHash`) y `PreviousAuditHash` — más el instante de firma (`SignedAtUtc`, el
"timestamp" del entregable) y la versión de clave, para que dos lotes idénticos firmados en instantes
distintos produzcan firmas distintas. Recalcular en lugar de confiar en el campo ya almacenado es deliberado:
`AuditEntry.AuditHash` es un campo más del registro, que un atacante con escritura directa al almacenamiento
podría dejar sin actualizar al alterar otro campo — recalcular sobre el contenido efectivo es lo que
garantiza que alterar **cualquier** campo de **cualquier** registro del lote invalida la firma, incluso en
ese caso límite (que, de todos modos, `IAuditIntegrityVerifier` seguiría detectando por su cuenta como
`HashMismatch`).

```csharp
var signResult = await auditBatchSigner.SignAsync(entriesDelLote, cancellationToken);
// ... más tarde, desde el mismo proceso o desde otro distinto:
var verifyResult = await auditBatchSigner.VerifyAsync(entriesDelLote, signResult.Value, cancellationToken);
if (verifyResult.IsSuccess && !verifyResult.Value)
{
    logger.LogCritical("Firma de lote de auditoría inválida -- el lote fue alterado después de firmarse.");
}
```

`VerifyAsync` devuelve `Result.Success(false)` (no una excepción ni un `Result.Failure`) cuando la firma es
sintácticamente válida pero no corresponde al lote/clave — mismo criterio que el resto de los resultados de
verificación del framework (`AuditIntegrityVerificationResult`). `Result.Failure` queda reservado para
condiciones que impiden intentar la verificación: lote vacío, o la versión de clave embebida en la firma ya
no existe en el proveedor de secretos.

### Registro: `AddSharedAuditBatchSigning`

```csharp
services.AddSharedSecretProvider(configuration);   // F2-12, debe registrarse antes
services.AddSharedAuditBatchSigning(configuration); // F2-17
```

Deliberadamente un método de registro **separado** de `AddSharedAuditing` — firmar lotes es opt-in (requiere
`ISecretProvider` ya registrado y que el proyecto consumidor haya decidido su propia política de "cuándo
firmar un lote") mientras que `AddSharedAuditing` no requiere ninguna configuración adicional para dejar
auditoría básica funcionando.

### `AuditBatchSignature.ToString()`/`TryParse` — serialización de conveniencia

`AuditBatchSignature` expone `ToString()` (formato `"v{KeyVersion}|{SignedAtUtc:O}|{Value}"`) y el estático
`TryParse` para que un proyecto consumidor persista la firma junto con una referencia al lote (por ejemplo,
en el destino WORM de F2-18) sin tener que definir su propio formato de serialización — F2-17 no persiste
nada por sí mismo (ver más abajo). `TryParse` devuelve `false` ante cualquier formato inválido, nunca lanza
una excepción — una firma serializada corrupta o manipulada es un resultado de negocio esperado para quien
la lea.

### Qué NO resuelve F2-17

- **No define CUÁNDO se firma un lote en producción** — cada N registros, cada X minutos, al cierre de un
  período contable, etc. Esa política es responsabilidad del proyecto consumidor; `IAuditBatchSigner` solo
  firma/verifica el lote que se le entregue.
- **No persiste ninguna firma** — `SignAsync` devuelve un `AuditBatchSignature` en memoria; guardarlo junto
  con una referencia al lote firmado es responsabilidad del `IAuditWriter` real que un proyecto conecte, o
  del destino WORM de F2-18.
- **No agrupa lotes automáticamente** — igual que `IAuditIntegrityVerifier.Verify`, `SignAsync`/`VerifyAsync`
  esperan que el llamador les entregue ya el lote correcto, en el orden correcto.
- **No ofrece una firma asimétrica** — ver "Por qué HMAC" arriba: si un caso de uso necesita que un tercero
  sin acceso al proveedor de secretos (ej. un auditor externo) verifique una firma con una clave pública, eso
  requiere una tarea nueva que introduzca gestión de claves asimétricas al framework.
- **No reemplaza a F2-16** — `IAuditIntegrityVerifier` sigue siendo el mecanismo de detección de
  manipulación de uso más frecuente y liviano (no requiere ninguna clave); `IAuditBatchSigner` es la capa
  adicional para el caso en que además se necesite una prueba resistente a la reconstrucción completa de la
  cadena por quien tiene acceso de escritura al almacenamiento.

## Exportación a almacenamiento WORM: `IWormStorage` e `IAuditWormExportPipeline` (F2-18)

### Qué hueco cierra, exactamente

`IAuditIntegrityVerifier` (F2-16) e `IAuditBatchSigner` (F2-17) detectan y prueban manipulación, pero
ninguna de las dos piezas anteriores IMPIDE la manipulación en primer lugar: ambas siguen operando sobre lo
que sea que devuelva el almacenamiento subyacente de `IAuditWriter` (en desarrollo, `InMemoryAuditWriter`;
en producción, lo que cada proyecto conecte). Un atacante -- o un operador con privilegios administrativos
excesivos -- con acceso de escritura a ESE almacenamiento puede alterar o eliminar datos antes de que
cualquier verificación posterior tenga oportunidad de ejecutarse. F2-18 cierra ese hueco con un destino de
exportación cuya propia semántica de almacenamiento rechaza la sobrescritura o eliminación mientras la
retención esté vigente -- no depende de ningún control de aplicación adicional, es una propiedad del
almacenamiento mismo.

### Dos capas: `IWormStorage` (primitiva genérica) e `IAuditWormExportPipeline` (política de retención)

```csharp
public interface IWormStorage
{
    Task<Result<WormObjectMetadata>> WriteAsync(WormWriteRequest request, CancellationToken cancellationToken = default);
    Task<Result<WormObject>> ReadAsync(string key, CancellationToken cancellationToken = default);
    Task<Result> DeleteAsync(string key, CancellationToken cancellationToken = default);
}
```

`IWormStorage` es deliberadamente genérica ("clave -> bytes con retención"), sin conocer nada de
`AuditEntry` -- el framework no tenía previamente ninguna abstracción de blob/object storage
(`IBlobStorage`/`IObjectStorage`) que F2-18 pudiera reutilizar, así que esta interfaz nace con esta tarea,
siguiendo el mismo patrón que `ISecretProvider` (F2-12) e `IEncryptionProvider` (F2-13): intercambiable por
implementación, sin que el código consumidor referencie nunca un tipo concreto.

Sobre esa primitiva, `IAuditWormExportPipeline` es la pieza que sí conoce `AuditEntry` y es el entregable
concreto de F2-18 ("Pipeline de retención"): serializa un lote ya escrito (`AuditWormBatchSerializer`,
JSON) y lo persiste vía `IWormStorage` aplicando una retención configurada centralmente
(`AuditWormExportOptions.RetentionPeriod`) en lugar de que cada llamador la calcule por su cuenta en cada
exportación:

```csharp
public interface IAuditWormExportPipeline
{
    Task<Result<WormObjectMetadata>> ExportAsync(AuditWormExportRequest request, CancellationToken cancellationToken = default);
    Task<Result<AuditWormExportedBatch>> ReadAsync(string key, CancellationToken cancellationToken = default);
}
```

```csharp
var exportResult = await auditWormExportPipeline.ExportAsync(
    new AuditWormExportRequest(key: $"{tenantId}/{DateTime.UtcNow:yyyy/MM/dd}/{batchId}", batch: loteFirmado, signature: firma));
// ... más tarde, para verificar/auditar (F2-20 futuro, o un proceso de cumplimiento propio):
var readResult = await auditWormExportPipeline.ReadAsync(exportResult.Value.Key);
```

### Semántica WORM que toda implementación debe cumplir

- **Write-once real, no solo durante la retención**: escribir con una clave ya usada falla SIEMPRE
  (`Worm.ObjectAlreadyExists`, `ErrorType.Conflict`), incluso si el objeto original ya fue eliminado tras
  expirar su retención -- permitir reutilizar una clave abriría la puerta a fabricar un reemplazo bajo el
  mismo identificador que un registro legítimo ya eliminado.
- **Retención bloquea eliminación, no lectura**: eliminar antes de `WormObjectMetadata.RetentionExpiresAtUtc`
  falla con un ERROR DE NEGOCIO (`Worm.RetentionPeriodNotExpired`, `ErrorType.Conflict`), nunca con una
  excepción no controlada. Leer nunca depende del estado de retención mientras el objeto no haya sido
  eliminado.
- **Eliminación después de expirar la retención SÍ es válida** -- WORM significa "eliminación prohibida
  mientras la retención esté vigente", no "eliminación prohibida para siempre"; cumplir una política de
  expurgo tras vencer la retención legal/regulatoria sigue soportado.

### Implementación de referencia: `InMemoryWormStorage` -- EXACTAMENTE el mismo criterio que `InMemoryAuditWriter`

`AddSharedAuditWormExport` registra `InMemoryWormStorage` como implementación por defecto de
`IWormStorage` (`TryAddSingleton`). Modela correctamente la semántica de arriba (probado en
`tests/Shared.Infrastructure.Security.Tests/Audit/Worm/InMemoryWormStorageTests.cs`: escritura+lectura
íntegra, rechazo de sobrescritura, rechazo de eliminación con retención vigente, eliminación válida tras
expirar, rechazo de reescritura de una clave ya eliminada) pero **no persiste entre reinicios ni entre
instancias del proceso** -- un objeto "inmutable" que desaparece al reiniciar el proceso no cumple ningún
objetivo real de retención regulatoria. Es un placeholder de desarrollo, no una fuente de verdad WORM
productiva, con el mismo tratamiento explícito que `InMemoryAuditWriter` (F2-15) y
`ConfigurationSecretProvider` (F2-12).

### Proveedor productivo real: pendiente de aprobación humana (ADR 0017, `Proposed`)

Igual que ocurrió con el proveedor de secretos (F2-12, ADR 0014), la elección de un backend WORM productivo
real es una decisión de infraestructura sujeta a la sección 13 del Plan Maestro ("nueva base de datos o
broker"). **ADR 0017** propone MinIO/S3 Object Lock (modo `COMPLIANCE`) como candidato -- imagen oficial de
contenedor apta para Testcontainers, self-hosteable, y con la semántica WORM ya resuelta por el propio
protocolo S3 -- pero lo deja explícitamente `Proposed`, no `Accepted`: esta tarea NO construye ese
proveedor, a la espera de la misma aprobación explícita que recibió Vault. Un proyecto que necesite
cumplimiento regulatorio real HOY debe conectar su propio `IWormStorage` (por ejemplo, contra un S3/MinIO/
Azure Blob Storage con Immutable Storage ya aprobado en su propia organización) registrándolo después de
`AddSharedAuditWormExport` -- gana la resolución, mismo principio que el resto de los `AddShared*`.

### Integración con F2-17: acoplada, decisión explícita

`AuditWormExportRequest` acepta un `AuditBatchSignature?` OPCIONAL: si el llamador firmó el lote con
`IAuditBatchSigner` (F2-17) antes de exportarlo, `AuditWormExportPipeline` serializa datos y firma JUNTOS en
el mismo objeto WORM (`AuditWormBatchSerializer`, formato JSON con `Entries` + `Signature` serializada vía
`AuditBatchSignature.ToString()`), y `ReadAsync` devuelve ambos de vuelta. Se eligió esta integración
ACOPLADA (en lugar de mantener exportación y firma completamente desacopladas, cada una en un objeto
distinto) porque:

- Un objeto WORM que contiene datos y prueba de integridad juntos es autocontenido -- un proceso de
  cumplimiento que lea SOLO del almacenamiento WORM (sin acceso al almacenamiento operacional de
  `IAuditWriter`) puede verificar la firma sin necesitar correlacionar dos objetos distintos.
- No es obligatorio: `Signature` es opcional en `AuditWormExportRequest` -- un proyecto que decida mantener
  firma y exportación desacopladas (por ejemplo, firmar una vez y exportar el mismo lote a más de un
  destino WORM) puede omitir el parámetro y manejar la correlación por su cuenta.

### Qué NO resuelve F2-18

- **No decide la política de retención concreta** (días/años) que corresponde a cada proyecto/regulación
  (SOX, PCI-DSS, normativa local de cada industria) -- `AuditWormExportOptions.RetentionPeriod` es un
  default configurable (placeholder de 7 años), no una recomendación normativa; cada proyecto consumidor
  fija el valor que corresponda a su propio marco regulatorio.
- **No implementa un proveedor productivo real** -- ver "Proveedor productivo real" arriba, ADR 0017 sigue
  `Proposed`.
- **No expone una API de consulta/lectura administrativa** -- `IAuditWormExportPipeline.ReadAsync` lee UN
  objeto por su clave exacta, no busca ni pagina; la API administrativa de búsqueda es F2-20.
- **No agrupa lotes ni genera claves automáticamente** -- mismo criterio que `IAuditBatchSigner` (F2-17):
  el llamador decide qué entra en un lote y qué clave usar para exportarlo.
- **No resuelve el modo de retención `GOVERNANCE` vs. `COMPLIANCE`** de un backend S3 real (si se aprobara
  ADR 0017) -- esa elección concreta queda para el trabajo de seguimiento que implemente el proveedor real.
- **No autoriza quién puede purgar un objeto tras expirar su retención** -- `IWormStorage.DeleteAsync` solo
  impone que el borrado sea rechazado MIENTRAS la retención esté vigente; una vez vencida, cualquier
  llamador puede eliminar sin ningún control de RBAC/ABAC ni traza de auditoría del propio intento de
  purga adicional a los que ya aplique la capa que invoca. Decidir quién puede invocar `DeleteAsync` y
  auditar ese intento es responsabilidad de la capa que orquesta `IAuditWormExportPipeline` (o que llama
  directamente a `IWormStorage`) -- mismo criterio que `ISecretProvider`/`IEncryptionProvider`, que tampoco
  autorizan por sí solos.

## Redacción de PII: `IAuditRedactionPolicy` y `RedactingAuditWriter` (F2-19)

### Qué hueco cierra, exactamente

`AuditEntryRequest.Metadata`/`Reason` son campos de texto libre (F2-15) -- necesarios para que un handler de
aplicación agregue contexto de negocio específico a un registro de auditoría, pero, por eso mismo, también
el lugar más fácil para que un desarrollador vuelque sin querer un dato sensible (una contraseña, un email,
un número de tarjeta/documento) que termina persistido en texto plano en un registro de retención larga --
potencialmente reexportado a WORM (F2-18) con una retención de años. F2-19 cierra ese hueco con una
clasificación explícita más una red de seguridad de contenido, aplicadas SIEMPRE antes de que el dato llegue
a persistirse.

### Dos mecanismos, en este orden de confiabilidad

```csharp
public interface IAuditRedactionPolicy
{
    AuditEntryRequest Redact(AuditEntryRequest request);
}
```

1. **Clasificación por nombre de clave** (`AuditRedactionOptions.SensitiveMetadataKeys`) -- la política
   PRINCIPAL. Toda clave de `Metadata` que matchee (comparación case-insensitive) contra esta lista se
   redacta SIEMPRE, sin importar su contenido. Es configurable por proyecto a propósito: "qué es PII" varía
   por jurisdicción y por dominio de negocio, así que el framework no puede fijar una lista cerrada y
   correcta para todos los consumidores. Los defaults de `AuditRedactionOptions` (`password`, `token`,
   `dni`, `cuit`/`cuil`, `email`, `tarjeta`, etc.) son un punto de partida razonable, NO una clasificación
   normativa ni exhaustiva -- cada proyecto debe revisar y extender esta lista según su propio marco
   regulatorio y su propio uso de `Metadata`.
2. **Detección de patrones de contenido** (`AuditRedactionOptions.EnableContentPatternDetection`, `true` por
   defecto) -- una red de seguridad ADICIONAL (*defense in depth*), aplicada a valores de `Metadata` cuya
   clave NO fue declarada sensible, y a `Reason` (que no es un diccionario, no tiene "clave" que
   clasificar). `AuditRedactionPolicy` (implementación por defecto) cubre dos patrones:
   - **Email**: `[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}`.
   - **Secuencia larga de dígitos (13-19), con o sin separadores de espacio/guion cada 4** -- el rango
     típico de un PAN de tarjeta (ISO/IEC 7812) y de varios documentos de identidad numéricos largos.

   **Un patrón de teléfono NO se incluyó a propósito**: el formato de un número de teléfono varía demasiado
   entre países (con/sin prefijo internacional, con/sin separadores) para un patrón único que no genere una
   tasa alta de falsos positivos/negativos -- se prefirió no incluir un patrón de baja confiabilidad antes
   que dar una falsa sensación de cobertura.

   **Esto es best-effort, NUNCA una garantía**: el reconocimiento de patrones de PII en texto libre no es
   100% confiable en ninguna implementación -- puede haber falsos negativos (un dato sensible en un formato
   no cubierto por ningún patrón) y, en menor medida, falsos positivos (un identificador de negocio legítimo
   que coincide por casualidad con un patrón, por ejemplo un ID numérico largo). La clasificación por nombre
   de clave sigue siendo el mecanismo confiable; la detección de contenido es un complemento, no un
   reemplazo de una revisión legal/de cumplimiento de qué constituye PII regulada en cada jurisdicción.

### Reemplazo, no pseudonimización: `RedactionPlaceholder`

Un valor clasificado como sensible se reemplaza por un placeholder fijo (`"[REDACTED]"` por defecto,
configurable) -- no un hash truncado ni ningún otro esquema reversible/correlacionable. Deliberado: el
criterio de aceptación de F2-19 es "logs sin PII no autorizada", no "PII pseudonimizada pero igual
reconstruible". Si un caso de uso futuro necesitara correlacionar valores redactados entre sí sin exponerlos
(por ejemplo, para detectar que dos registros distintos comparten el mismo email sin poder leerlo), eso es
una extensión posterior explícita fuera del alcance mínimo de esta tarea.

### Dónde se aplica: `RedactingAuditWriter`, decorador de `IAuditWriter`

```csharp
public sealed class RedactingAuditWriter(IAuditWriter inner, IAuditRedactionPolicy redactionPolicy) : IAuditWriter
{
    public Task<Result<AuditEntry>> WriteAsync(AuditEntryRequest request, CancellationToken cancellationToken = default) =>
        inner.WriteAsync(redactionPolicy.Redact(request), cancellationToken);
}
```

Un decorador de `IAuditWriter` -- mismo patrón que `CachedPermissionService` (F2-09) sobre
`IPermissionService` -- en lugar de modificar `InMemoryAuditWriter` directamente: la redacción debe aplicar
igual a CUALQUIER implementación futura de `IAuditWriter` (tabla SQL, event store, o el destino WORM que un
proyecto conecte), no solo a la implementación de desarrollo.

### Orden de operaciones, CRÍTICO respecto de F2-16/F2-17

La redacción ocurre ANTES de que el `AuditEntryRequest` llegue al `IAuditWriter` real -- el punto donde
`AuditHashCalculator.Compute` calcula `AuditEntry.AuditHash` (F2-15/F2-16) y donde, más tarde, un lote se
firma (F2-17) o se exporta a WORM (F2-18). Este orden no es un detalle de implementación intercambiable:

- Si se redactara DESPUÉS de calcular el hash (por ejemplo, mutando un `AuditEntry` ya construido sin
  recalcular), el `AuditHash` almacenado dejaría de corresponder al contenido efectivamente persistido, y
  `IAuditIntegrityVerifier.Verify` reportaría un falso `HashMismatch` sobre un registro que en realidad
  nunca fue manipulado por un tercero -- se rompería F2-16.
- Si el hash/firma se calculara sobre el dato SIN redactar y solo se redactara la vista final (por ejemplo,
  al leer), el propio hash/la propia firma seguiría siendo, en la práctica, información derivada del valor
  sensible original -- no cumpliría el objetivo de F2-19 de raíz, y además cualquier destino que reciba el
  lote firmado (WORM, F2-18) recibiría igual el dato sin redactar.

Decorando `IAuditWriter` en la capa MÁS EXTERNA (envuelve el escritor real, cualquiera sea) se garantiza que
NINGÚN `IAuditWriter` recibe jamás el valor sin redactar, y que el hash/firma calculados corresponden
siempre a los datos YA redactados -- verificado en
`tests/Shared.Infrastructure.Security.Tests/Audit/RedactingAuditWriterTests.cs` (el `AuditHash` persistido
coincide con `AuditHashCalculator.Compute` sobre el contenido redactado, no sobre el original; la cadena de
integridad de F2-16 sigue siendo válida) y en
`tests/Shared.Infrastructure.Security.Tests/Audit/Worm/AuditRedactionWormExportTests.cs` (un lote exportado
a WORM contiene los datos redactados al leerlo de vuelta).

### Registro: `AddSharedAuditRedaction`

```csharp
services.AddSharedAuditing();                              // F2-15, debe registrarse antes
services.AddSharedAuditRedaction(configuration);            // F2-19
```

Deliberadamente un método SEPARADO de `AddSharedAuditing` -- mismo principio que
`AddSharedPermissionCache` (F2-09) sobre `AddSharedPermissionEvaluation`: decora el `IAuditWriter` ya
registrado (`TryAddSingleton<IAuditRedactionPolicy, AuditRedactionPolicy>` + `Replace` sobre `IAuditWriter`),
lanzando `InvalidOperationException` en el arranque si `IAuditWriter` todavía no fue registrado. Idempotente
(una segunda llamada no vuelve a decorar -- ver "Idempotencia" más abajo). Lee la sección de configuración
`AuditRedactionOptions.SectionName` ("AuditRedaction") -- opcional, los defaults ya son válidos sin
configuración explícita.

Un proyecto con su propia política de clasificación de PII (por ejemplo, integrada con una herramienta de
DLP externa) puede reemplazar `AuditRedactionPolicy` registrando su propia `IAuditRedactionPolicy` después
de `AddSharedAuditRedaction` -- gana la resolución, mismo principio que el resto de los `AddShared*`.

### Conectar el `IAuditWriter` REAL del proyecto: `AddAuditWriter<TWriter>`, no `AddScoped` manual

Fix post-revisión de arquitectura de F2-19 (Hallazgo 1, CRÍTICO). El registro habitual de un `IAuditWriter`
productivo (tabla SQL append-only, event store, destino WORM propio) es un `AddScoped`/`Replace` manual que
gana la resolución sobre cualquier registro anterior -- funciona bien cuando el orden es
`AddSharedAuditing()` → `AddSharedAuditRedaction(...)` → registro manual del writer real (el caso ya cubierto
antes de este fix). El problema aparece con el orden INVERSO, exactamente igual de válido a simple vista y
justamente el que corresponde al caso de uso de producción más común (un proyecto que ya conecta su propio
writer persistente):

```csharp
// NO HACER: deja el RedactingAuditWriter huérfano, SIN ningún error visible en el arranque.
services.AddSharedAuditing();
services.AddSharedAuditRedaction(configuration);
services.AddScoped<IAuditWriter, SqlAuditWriter>();   // <- este gana la resolución, sin decorar
```

Con ese orden, el `AddScoped` final reemplaza el registro de `IAuditWriter` -- el `RedactingAuditWriter` que
`AddSharedAuditRedaction` acababa de fijar queda sin ningún consumidor, sin lanzar ninguna excepción.
`SqlAuditWriter` pasa a recibir el `AuditEntryRequest` **sin redactar**, `AuditHashCalculator.Compute` calcula
`AuditEntry.AuditHash` sobre esa PII sin redactar, y cualquier firma (F2-17) o exportación WORM (F2-18)
posterior hereda esa PII sin redactar -- silenciosamente.

La forma correcta de conectar el writer real, que hace que el ORDEN relativo entre ambas llamadas deje de
importar, es `AddAuditWriter<TWriter>`:

```csharp
services.AddSharedAuditing();
services.AddSharedAuditRedaction(configuration);
services.AddAuditWriter<SqlAuditWriter>();   // <- detecta la redacción ya aplicada y la vuelve a aplicar
```

```csharp
// Orden inverso -- funciona exactamente igual, AddAuditWriter no depende de cuál se llamó primero.
services.AddSharedAuditing();
services.AddAuditWriter<SqlAuditWriter>();
services.AddSharedAuditRedaction(configuration);
```

`AddAuditWriter<TWriter>` registra `TWriter` (lifetime configurable, `Scoped` por defecto) y comprueba
(vía un marcador interno, `AuditRedactionAppliedMarker`, fijado por `AddSharedAuditRedaction`) si la
redacción ya fue aplicada en algún momento anterior de la composición de servicios:

- Si SÍ fue aplicada, vuelve a envolver `TWriter` en un `RedactingAuditWriter` -- la redacción sigue
  aplicándose sin importar que `TWriter` se haya registrado después.
- Si NO fue aplicada (todavía, o nunca), registra `TWriter` como `IAuditWriter` sin decorar, igual que un
  `AddScoped`/`Replace` manual habría hecho -- un proyecto que decide no usar `AddSharedAuditRedaction` en
  absoluto no ve ningún cambio de comportamiento.

Esto resuelve el hueco DE RAÍZ (el orden deja de ser una responsabilidad del consumidor), en vez de solo
detectarlo o documentarlo -- verificado en
`tests/Shared.Infrastructure.Security.Tests/Audit/AuditRedactionServiceCollectionExtensionsTests.cs`
(`AddAuditWriter_RegistradoDespuesDeAddSharedAuditRedaction_SigueRedactando`), que reproduce exactamente el
escenario de falla descrito arriba.

### Idempotencia: marcador dedicado, no inspección del descriptor de `IAuditWriter`

Fix post-revisión de arquitectura de F2-19 (Hallazgo 2). La comprobación de idempotencia original de
`AddSharedAuditRedaction` (`d.ImplementationType == typeof(RedactingAuditWriter)`) nunca era verdadera: el
decorador se registra vía `ServiceDescriptor.Describe` con una factory, no con un tipo concreto, y para un
descriptor basado en factory `ServiceDescriptor.ImplementationType` es siempre `null` -- mismo motivo por el
que `PermissionEvaluationServiceCollectionExtensions.IsPermissionCacheAlreadyApplied` (F2-09) usa
`ServiceDescriptor.ImplementationFactory is not null` como marcador en vez de `ImplementationType`. Como
consecuencia, llamar a `AddSharedAuditRedaction` dos veces SÍ volvía a decorar (doble envoltorio
`RedactingAuditWriter(RedactingAuditWriter(inner))`) -- funcionalmente inofensivo (redactar dos veces un
valor ya reemplazado por el placeholder no cambia el resultado) pero desperdiciado.

El fix usa un marcador interno dedicado (`AuditRedactionAppliedMarker`, `TryAddSingleton`) en vez de
`ImplementationFactory is not null`: a diferencia del caso de `IPermissionService`, acá
`AddAuditWriter<TWriter>` TAMBIÉN registra `IAuditWriter` con una factory en el caso NO decorado, así que
comprobar solo "hay una factory" sería ambiguo entre "ya está decorado" y "hay un writer real registrado sin
decorar todavía". El test de regresión
(`AddSharedAuditRedaction_LlamadaDosVeces_NoVuelveADecorar`) ya no cuenta descriptores de `IAuditWriter`
después de `Replace` (que siempre deja `count == 1`, sin importar cuántas veces se llame -- el test anterior
daba una falsa confianza) sino que verifica el comportamiento real: contando cuántas veces se invoca
`IAuditRedactionPolicy.Redact` en una sola escritura.

### Alcance: `Metadata` y `Reason`, no el resto de los campos de `AuditEntryRequest`

F2-19 aplica exclusivamente a `Metadata` (clasificación + contenido) y `Reason` (solo contenido, no tiene
clave). Deliberadamente NO toca `Actor.Id`, `Resource.Id`, `CorrelationId`, `TraceId` ni `IpAddress`:
- Son identificadores estructurados con un propósito de correlación/trazabilidad que la propia auditoría
  necesita preservar sin redactar -- redactar `IpAddress`, en particular, inutilizaría la auditoría como
  evidencia de una investigación de seguridad, que es justamente uno de sus propósitos centrales.
- El vector de riesgo real que motiva F2-19 son los campos de texto libre pensado para contexto de negocio
  arbitrario (`Metadata`/`Reason`), no los campos identificadores ya acotados por el propio modelo de F2-15.

Esto reproduce exactamente la distinción que ya dejó escrita el comentario XML de
`AuditEntryRequest.Metadata` desde F2-15/F2-18 ("Nunca debe contener PII/datos sensibles sin redactar... la
política de redacción formal es F2-19").

### Qué NO resuelve F2-19

- **No garantiza remoción del 100% de la PII no declarada**: la detección de patrones de contenido es
  best-effort -- puede haber falsos negativos (formato no cubierto por ningún patrón: un DNI de 7-8 dígitos
  de un país que no entra en el rango 13-19, un teléfono en cualquier formato) y, en menor medida, falsos
  positivos. La clasificación por nombre de clave sigue siendo el mecanismo confiable; declarar
  explícitamente cada clave que un caso de uso conoce como sensible sigue siendo responsabilidad del
  proyecto consumidor.
- **No reemplaza una revisión legal/de cumplimiento**: qué constituye PII regulada (GDPR, normativa local de
  protección de datos, PCI-DSS para datos de tarjeta) varía por jurisdicción e industria -- los defaults de
  `AuditRedactionOptions.SensitiveMetadataKeys` son un punto de partida, no una certificación de
  cumplimiento.
- **No aplica a los demás logs de la aplicación fuera del subsistema de auditoría**: esta política vive
  exclusivamente en `IAuditWriter`/`AuditEntryRequest` (`Shared.Infrastructure.Security.Audit`). El
  framework, al día de esta tarea, no tiene ningún mecanismo de logging estructurado general (Serilog
  destructuring policies, atributos `[Redact]`/`[Sensitive]`, etc.) con el que integrar o del que reutilizar
  esta política -- extender el mismo criterio de clasificación/redacción a los logs generales de la
  aplicación (fuera de auditoría) es una tarea/decisión aparte, no resuelta acá.
- **No pseudonimiza de forma correlacionable**: ver "Reemplazo, no pseudonimización" arriba -- el valor
  redactado no es recuperable ni comparable entre registros distintos.
- **No redacta `Actor.Id`/`Resource.Id`/`CorrelationId`/`TraceId`/`IpAddress`**: ver "Alcance" arriba --
  decisión deliberada, no un descuido.

## Uso desde un handler de aplicación

```csharp
await auditWriter.WriteAsync(new AuditEntryRequest(
    actor: new AuditActor(currentUserProvider.UserId!, AuditActorType.User),
    tenantId: tenantContext.TenantId,
    action: "pedidos.eliminar",
    resource: new AuditResource("pedidos", pedidoId.ToString()),
    outcome: AuditOutcome.Success,
    correlationId: correlationId,
    traceId: traceId), cancellationToken);
```

Igual que `AbacResource` (F2-08), el llamador arma `AuditActor`/`AuditResource` explícitamente a partir de
datos ya resueltos — no hay ninguna resolución automática desde `HttpContext` dentro de este módulo.

## Integraciones cableadas (cierre del pendiente explícito de F2-15)

El pendiente dejado por F2-15 ("operaciones privilegiadas de F2-10 y cambios de permisos/roles de
F2-07/F2-09 son candidatas obvias a emitir auditoría y hoy no lo hacen") quedó cableado así:

| Origen | Punto de cableado | Acción auditada | Outcome |
|---|---|---|---|
| F2-10 (step-up authentication) | `AuditingAuthorizationPolicyEvaluator`, decorador de `IAuthorizationPolicyEvaluator` registrado por `AddSharedPrivilegedOperationsPolicies` | `"{resourceType}.{action}"` de toda evaluación cuyo (tipo de recurso, acción) tenga al menos un `StepUpRequirement` aplicable | `Success` (step-up satisfecho) / `Denied` (evidencia faltante o vencida — `Reason` con el código de `StepUpAbacRule`) |
| F2-10 (segregación de funciones) | mismo decorador | ídem, para toda evaluación con un `MakerCheckerRule` aplicable, o CUALQUIER evaluación si hay al menos un `MutuallyExclusivePermissionPair` configurado (restricción de identidad, no depende del recurso/acción) | `Success` / `Denied` (`Reason` con el código de `SegregationOfDutiesAbacRule`, por ejemplo `sod:same-actor:...` o `sod:mutually-exclusive-permissions:...`) |
| F2-07/F2-09 (alta/baja de permiso de un rol) | `RoleManagerPermissionExtensions.AddPermissionAsync`/`RemovePermissionAsync`, overload que recibe `IAuditWriter`+`AuditActor` | `"roles.permission.grant"` / `"roles.permission.revoke"`, recurso `"roles"` con `Id` = `role.Id`, metadata `permission`/`roleName` | `Success` / `Error` (fallo técnico de Identity, por ejemplo un conflicto de concurrencia — `Reason` con la descripción de `IdentityResult.Errors`) |

`AuditingAuthorizationPolicyEvaluator` decora el evaluador combinado real (RBAC + ABAC + privilegiadas)
sin crear un pipeline paralelo — SIEMPRE delega la decisión y nunca la altera; un fallo transitorio de
`IAuditWriter.WriteAsync` (un `Result` fallido) no bloquea ni cambia la decisión de autorización ya
tomada. Deliberadamente NO audita cada evaluación ABAC genérica de F2-08 (`AttributeScopeAbacRule`,
`AmountLimitAbacRule`) — solo aquella que efectivamente esté protegida por una política de operaciones
privilegiadas configurada, para no generar ruido de auditoría sobre operaciones que el Plan Maestro no
clasifica como críticas.

`AddSharedPrivilegedOperationsPolicies` (F2-10) llama internamente a `AddSharedAuditing` si todavía no fue
llamado (idempotente, mismo criterio que el resto de los `AddShared*`) — un proyecto que adopta step-up o
segregación de funciones obtiene auditoría cableada sin un paso de registro adicional. El overload de
`RoleManagerPermissionExtensions` con `IAuditWriter` es opt-in: un proyecto que gestiona permisos de rol
sin ese overload sigue funcionando exactamente igual que antes (sin auditoría de esa operación puntual),
la migración al overload auditado es responsabilidad del código de aplicación que invoca esos métodos.

### Qué NO quedó cableado todavía

- **Alta/baja de un ROL completo** (`RoleManager.CreateAsync`/`DeleteAsync`) y la asignación de un rol a un
  usuario (`UserManager.AddToRoleAsync`/`RemoveFromRoleAsync`) no tienen un overload auditado — solo el
  alta/baja de un permiso individual dentro de un rol ya existente. Candidata para una tarea de
  seguimiento si el gate de Fase 2 lo exige explícitamente.
- **Cambios directos sobre `ApplicationUser`** (creación de usuario, bloqueo/desbloqueo, cambio de
  contraseña) siguen sin auditoría cableada — fuera del alcance literal de F2-07/F2-09/F2-10 (RBAC/ABAC/
  operaciones privilegiadas), no de gestión de identidad de usuario.
- **Invalidación de cache de permisos** (F2-09, `IPermissionCacheInvalidator`) no emite auditoría propia —
  es un efecto secundario técnico de la operación ya auditada (alta/baja de permiso), no una operación de
  negocio distinta.

## Pendiente explícito (fuera de alcance de F2-15/este cierre)

- **Almacenamiento persistente real**: la elección definitiva (tabla SQL append-only vs. event store vs.
  otro destino) es una decisión arquitectónica pendiente de un ADR propio — ver sección "Decisiones" del
  reporte de cierre de F2-15.
- **Lectura/consulta administrativa**: es F2-20; `InMemoryAuditWriter.Entries` no es esa API.

F2-19 (redacción de PII) ya no es un pendiente — ver la sección "Redacción de PII" arriba. La metadata que
el cierre de F2-15 agregó (`abacDecisionReason`, `permission`, `roleName`) son identificadores/códigos de
negocio, no PII, así que no requieren clasificación adicional en `AuditRedactionOptions` por defecto.

## Referencias

- `src/Shared.Infrastructure.Security/Audit/` — implementación (`AuditEntry`, `AuditEntryRequest`,
  `AuditActor`, `AuditResource`, `AuditHashCalculator`, `IAuditWriter`, `InMemoryAuditWriter`,
  `AuditServiceCollectionExtensions`, y de F2-16: `IAuditIntegrityVerifier`, `AuditIntegrityVerifier`,
  `AuditIntegrityVerificationResult`, `AuditIntegrityBreakReason`; y de F2-17: `IAuditBatchSigner`,
  `HmacAuditBatchSigner`, `AuditBatchSignature`, `AuditBatchSigningOptions`,
  `AuditBatchSigningServiceCollectionExtensions`; y de F2-19: `IAuditRedactionPolicy`,
  `AuditRedactionPolicy`, `AuditRedactionOptions`, `RedactingAuditWriter`,
  `AuditRedactionServiceCollectionExtensions.AddSharedAuditRedaction`/`AddAuditWriter`, y el marcador interno
  `AuditRedactionAppliedMarker` del fix post-revisión de arquitectura descrito arriba).
- `src/Shared.Infrastructure.Security/Audit/Worm/` — implementación de F2-18 (`IWormStorage`,
  `WormWriteRequest`, `WormObjectMetadata`, `WormObject`, `InMemoryWormStorage`,
  `IAuditWormExportPipeline`, `AuditWormExportPipeline`, `AuditWormExportRequest`,
  `AuditWormExportedBatch`, `AuditWormBatchSerializer`, `AuditWormExportOptions`,
  `AuditWormExportServiceCollectionExtensions.AddSharedAuditWormExport`).
- `tests/Shared.Infrastructure.Security.Tests/Audit/Worm/` — suite de pruebas de F2-18
  (`InMemoryWormStorageTests`: escritura/lectura íntegra, rechazo de sobrescritura, rechazo de eliminación
  con retención vigente, eliminación válida tras expirar, rechazo de reescritura de clave ya eliminada;
  `AuditWormExportPipelineTests`: exportación+lectura con contenido exactamente coincidente, integración
  con la firma de F2-17, lote vacío, retención por defecto vs. explícita por lote, propagación de
  conflictos de `IWormStorage`; `AuditWormExportServiceCollectionExtensionsTests`: registro por defecto,
  singleton, reemplazo por un proyecto consumidor, lectura de configuración).
- `docs/adr/0017-worm-proveedor-minio-object-lock-propuesto.md` — `IWormStorage` (`Accepted`) + MinIO/S3
  Object Lock como proveedor concreto (`Proposed`, pendiente de aprobación humana, sección 13).
- `tests/Shared.Infrastructure.Security.Tests/Audit/HmacAuditBatchSignerTests.cs` — suite de pruebas de
  F2-17 (firma/verificación válida con instancias distintas, lote vacío, alteración de cualquier campo,
  reconstrucción completa y consistente de la cadena alternativa -- el hueco explícito de F2-16 --,
  reordenamiento, firma/clave incorrecta, rotación de clave activa, serialización).
- `src/Shared.Infrastructure.Security/PrivilegedOperations/AuditingAuthorizationPolicyEvaluator.cs` —
  cableado de auditoría sobre step-up/segregación de funciones (F2-10).
- `src/Shared.Infrastructure.Security/Permissions/RoleManagerPermissionExtensions.cs` — cableado de
  auditoría sobre alta/baja de permiso de un rol (F2-07/F2-09).
- `tests/Shared.Infrastructure.Security.Tests/Audit/AuditIntegrityVerifierTests.cs` — suite de pruebas de
  F2-16 (cadena válida, hash alterado, registro eliminado/reordenado, cadena vacía, un solo registro).
- `tests/Shared.Infrastructure.Security.Tests/Audit/` — suite de pruebas (campos críticos completos,
  determinismo/sensibilidad del hash, ausencia de update/delete).
- `tests/Shared.Infrastructure.Security.Tests/PrivilegedOperations/AuditingAuthorizationPolicyEvaluatorTests.cs`
  y `PrivilegedOperationsEndToEndTests.cs` — suite de pruebas del cableado de F2-10.
- `tests/Shared.Infrastructure.Security.Tests/RoleManagerPermissionExtensionsTests.cs` — suite de pruebas
  del cableado de F2-07/F2-09.
- `tests/Shared.Infrastructure.Security.Tests/Audit/AuditRedactionPolicyTests.cs` — suite de pruebas de
  F2-19 sobre `AuditRedactionPolicy` aislada (clasificación por clave case-insensitive, detección de
  patrones de email/tarjeta-documento en `Metadata`/`Reason`, deshabilitación de la detección de patrones,
  placeholder configurable, valores `null`, inmutabilidad del request original).
- `tests/Shared.Infrastructure.Security.Tests/Audit/RedactingAuditWriterTests.cs` — suite de pruebas de
  F2-19 con `IAuditWriter` real (`InMemoryAuditWriter`): el registro persistido contiene el valor redactado,
  el `AuditHash` persistido corresponde al contenido YA redactado (no al original), la cadena de integridad
  de F2-16 sigue siendo válida sobre registros redactados, propagación de un fallo del writer interno.
- `tests/Shared.Infrastructure.Security.Tests/Audit/AuditRedactionServiceCollectionExtensionsTests.cs` —
  suite de pruebas de registro de F2-19 (decoración del `IAuditWriter` ya registrado, excepción si no hay
  ninguno, idempotencia real -- no solo conteo de descriptores --, lectura de configuración de claves
  sensibles adicionales, reemplazo de `IAuditRedactionPolicy` por un proyecto consumidor, y el escenario de
  regresión del fix post-revisión de arquitectura: `AddAuditWriter<TWriter>` registrado DESPUÉS de
  `AddSharedAuditRedaction` sigue redactando).
- `tests/Shared.Infrastructure.Security.Tests/Audit/Worm/AuditRedactionWormExportTests.cs` — prueba de
  extremo a extremo de F2-19+F2-18: un lote escrito a través de `RedactingAuditWriter` y exportado a WORM
  contiene, al leerlo de vuelta, los datos redactados.
