# 0017. WORM de auditoría: abstracción `IWormStorage` (Accepted) y MinIO/S3 Object Lock propuesto como proveedor

**Estado:** Proposed
**Fecha:** 2026-09-06
**Responsable de aprobación:** pendiente (Plan Maestro sección 13 — "nueva base de datos o broker"/infraestructura de almacenamiento productiva)

## Contexto

El Plan Maestro (Fase 2, Épica F2-D — Auditoría inmutable, F2-18: "WORM — Exportar a almacenamiento
inmutable", entregable "Pipeline de retención", criterio de aceptación "Escritura y lectura probadas")
pide un destino de exportación con semántica WORM (Write-Once-Read-Many) para los lotes de auditoría ya
escritos (F2-15), encadenados (F2-16) y opcionalmente firmados (F2-17, ADR 0016). El framework no exponía
previamente ninguna abstracción de blob/object storage (`IBlobStorage`/`IObjectStorage`) que F2-18 pudiera
reutilizar — esta tarea la introduce.

Igual que ocurrió con el proveedor de secretos (F2-12, ADR 0014), la elección de un backend de
almacenamiento WORM productivo real es una decisión de infraestructura con costo operativo (aprovisionar,
mantener HA/durabilidad, gestionar retención legal a escala de años) que el Plan Maestro sección 13 trata
explícitamente como sujeta a aprobación humana — acá con dos agravantes propios de esta tarea concreta:
"nueva base de datos o broker" (introducir un almacén de objetos nuevo al set de infraestructura del
framework) y una decisión de arquitectura que afecta directamente el cumplimiento regulatorio (retención de
auditoría) de cualquier proyecto consumidor.

## Decisión

Se separan dos decisiones de distinto nivel, mismo patrón que ADR 0014 (Vault) y ADR 0004 (adapter OIDC):

1. **La abstracción (`IWormStorage`, intercambiable por implementación) queda `Accepted` e implementada.**
   `Shared.Infrastructure.Security.Audit.Worm.IWormStorage` (`WriteAsync`/`ReadAsync`/`DeleteAsync` sobre
   `Result`/`Result<T>`, nunca excepciones para las condiciones de negocio) es el único contrato que
   `AuditWormExportPipeline` (F2-18) consume — nunca un tipo concreto. `AddSharedAuditWormExport` registra
   la implementación por defecto y un proyecto consumidor reemplaza esa resolución registrando la suya
   propia DESPUÉS (último registro gana, mismo principio que `AddSharedAuditing`/F2-15).
2. **`InMemoryWormStorage` (placeholder de desarrollo) queda `Accepted`** como implementación por defecto,
   con el mismo criterio y las mismas limitaciones que `InMemoryAuditWriter` (F2-15): modela correctamente
   la semántica de negocio (write-once permanente, retención bloquea eliminación prematura, eliminación
   válida tras expirar la retención — ver pruebas en `tests/Shared.Infrastructure.Security.Tests/Audit/Worm/InMemoryWormStorageTests.cs`)
   pero no persiste entre reinicios ni entre instancias del proceso — NO es una fuente de verdad WORM
   productiva.
3. **MinIO con Object Lock (API compatible S3) queda PROPUESTO, no decidido**, como candidato de proveedor
   concreto de nivel empresarial para un `IWormStorage` productivo real. Esta tarea NO implementa ese
   proveedor (ver "Qué NO resuelve F2-18" más abajo) — queda a la espera de la aprobación humana explícita
   de la sección 13 antes de construirse, exactamente el mismo tratamiento que recibió Vault en ADR 0014
   antes de su aprobación.

## Por qué se propone MinIO/S3 Object Lock (y no se implementó ya)

- **Object Lock es la semántica WORM real, ya resuelta por el protocolo S3**: modo de retención
  `COMPLIANCE` (nadie, ni siquiera una cuenta con privilegios de administración, puede eliminar o
  sobrescribir un objeto antes de que expire su retención) o `GOVERNANCE` (una eliminación anticipada
  requiere un permiso especial explícito, auditable por separado) — exactamente la garantía que
  `IWormStorage.DeleteAsync` documenta como obligatoria para cualquier implementación real.
- **Imagen oficial de contenedor apta para Testcontainers/CI** (`minio/minio`), consistente con el criterio
  ya usado para Vault (ADR 0014) y Keycloak (ADR 0004): self-hosteable, sin atar el entorno de referencia a
  un tenant cloud de pago.
- **API compatible S3 sin SDK propietario pesado** — se integra con cualquier cliente S3 estándar (AWS SDK
  para .NET u otro), consistente con el estilo de integración vía protocolo estándar que ya usan
  `VaultSecretProvider` (API HTTP) y `OidcAuthorizationCodeExchanger` (F2-02/F2-04).
- **No se implementó en esta tarea** por la misma razón documentada para Vault en ADR 0014: aprovisionar un
  backend de Object Lock real (bucket con versionado + Object Lock habilitado desde la creación del bucket
  -- una limitación propia de S3/MinIO, no se puede activar retroactivamente sobre un bucket existente) es
  trabajo de infraestructura que excede el alcance mínimo de F2-18 y que la sección 13 del Plan Maestro
  exige someter a aprobación antes de construirse — igual que la elección de Vault se propuso primero y se
  aprobó después de forma explícita.

## Alternativas consideradas

- **Azure Blob Storage con Immutable Storage (time-based retention/legal hold):** semántica WORM
  equivalente a Object Lock, pero ningún ADR previo fija Azure como plataforma cloud objetivo (mismo motivo
  que descartó Azure Key Vault en ADR 0014) — candidato igualmente válido si el responsable de la decisión
  ya opera en Azure, pero no es el candidato por defecto de este ADR por consistencia con el resto del
  framework (self-hosteable primero).
- **AWS S3 con Object Lock real (no MinIO):** mismo protocolo/API que el candidato propuesto, así que
  `IWormStorage` implementado contra MinIO es portable a S3 real casi sin cambios (ambos hablan el mismo
  protocolo) — no se excluye, se documenta como una variante de despliegue de la misma decisión de
  protocolo, no una decisión de proveedor distinta.
- **Tabla SQL con triggers de "deny UPDATE/DELETE" + una columna de expiración de retención:** viable como
  mitigación de bajo costo (sin infraestructura nueva) pero más débil que Object Lock: un rol con permisos
  de esquema (`ALTER TABLE`/`DROP TRIGGER`) puede desactivar la protección — Object Lock en modo
  `COMPLIANCE` no tiene ese punto de fuga ni para una cuenta con privilegios administrativos sobre el
  propio objeto. Queda como alternativa de menor costo para un proyecto que no pueda operar MinIO/S3.
- **No implementar ninguna abstracción todavía, esperar la decisión de proveedor:** descartada -- el
  criterio de aceptación de F2-18 ("Escritura y lectura probadas") exige algo ejecutable y probado ahora;
  `IWormStorage` + `InMemoryWormStorage` (mismo criterio que `ISecretProvider` + `ConfigurationSecretProvider`
  en F2-12) permite dejar el contrato listo y probado sin esperar la aprobación de infraestructura.

## Consecuencias

- **F2-18 implementado:** `src/Shared.Infrastructure.Security/Audit/Worm/` (`IWormStorage`, `WormWriteRequest`,
  `WormObjectMetadata`, `WormObject`, `InMemoryWormStorage`, `IAuditWormExportPipeline`,
  `AuditWormExportPipeline`, `AuditWormExportRequest`, `AuditWormExportedBatch`, `AuditWormBatchSerializer`,
  `AuditWormExportOptions`, `AuditWormExportServiceCollectionExtensions.AddSharedAuditWormExport`). Ver
  `docs/guia-auditoria-inmutable.md`, sección F2-18, para el detalle de diseño.
- Mientras este ADR siga `Proposed`, ningún proyecto consumidor tiene un `IWormStorage` productivo real
  ofrecido por el framework — solo el placeholder en memoria. Un proyecto que necesite cumplimiento
  regulatorio real sobre retención de auditoría HOY debe implementar su propio `IWormStorage` (por ejemplo,
  contra un S3/MinIO/Azure Blob ya aprobado en su propia organización) hasta que este ADR se apruebe.
- Si se aprueba MinIO/S3 Object Lock como proveedor concreto, el trabajo de seguimiento incluye: cliente S3
  real (`MinioWormStorage` o similar), aprovisionamiento del bucket con versionado + Object Lock habilitados
  desde su creación, mapeo de `WormWriteRequest.RetentionPeriod` a un modo de retención (`COMPLIANCE` vs.
  `GOVERNANCE` -- esta elección en sí también amerita registrarse como una decisión explícita, dado que
  `GOVERNANCE` reintroduce un punto de fuga controlado), y pruebas de integración vía Testcontainers
  (imagen `minio/minio`) equivalentes a `VaultContainerFixture`.

## Riesgos y mitigación

- **Riesgo:** que un proyecto consumidor asuma que `InMemoryWormStorage` ya es "WORM productivo" solo
  porque pasa las pruebas de semántica. Mitigado: la documentación de la clase, `docs/guia-auditoria-inmutable.md`
  y este ADR lo dejan explícito, mismo patrón que `InMemoryAuditWriter`/`ConfigurationSecretProvider`.
- **Riesgo:** construir el proveedor real (MinIO/S3) sin la aprobación humana de la sección 13. Mitigación:
  este ADR queda explícitamente `Proposed`, no `Accepted`, hasta que se registre una aprobación explícita
  con fecha y responsable (mismo mecanismo que formalizó ADR 0014 para Vault).
- **Riesgo:** que la política de retención concreta (años) que cada proyecto/regulación exige nunca se
  documente y cada equipo la fije de forma ad hoc. Mitigación parcial: `AuditWormExportOptions.RetentionPeriod`
  es explícito y configurable, con un default documentado como placeholder no normativo (ver "Qué NO
  resuelve F2-18"); la política concreta sigue siendo responsabilidad de cada proyecto.
- Vinculado al registro de riesgos de F0-12 y a la sección 13 del Plan Maestro.
