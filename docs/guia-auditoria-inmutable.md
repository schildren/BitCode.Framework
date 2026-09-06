# Guía — Auditoría inmutable: `IAuditWriter` (F2-15)

**Tarea:** F2-15 (Fase 2, Épica F2-D — Auditoría inmutable) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Entregable:** esquema append-only.
**Criterio de aceptación:** "Campos críticos completos".

F2-15 es la base de datos/modelo de la Épica F2-D. Las tareas siguientes se apoyan directamente en este
esquema sin requerir un cambio de contrato público breaking:

- **F2-16** (cadena de integridad): completa `AuditEntry.PreviousAuditHash`, ya reservado en null desde
  F2-15.
- **F2-17** (firma y timestamp): firma lotes/eventos de auditoría ya escritos por `IAuditWriter`.
- **F2-18** (WORM): un destino de exportación inmutable, consumiendo las entradas ya escritas.
- **F2-19** (PII y redacción): política de qué puede/no puede volcarse en
  `AuditEntryRequest.Metadata`/`Reason`.
- **F2-20** (consulta de auditoría): una API de lectura sobre el almacenamiento real detrás de
  `IAuditWriter` (F2-15 no incluye ningún mecanismo de lectura más allá de
  `InMemoryAuditWriter.Entries`, pensado solo para inspección en pruebas/desarrollo local).

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
| `PreviousAuditHash` | Reservado para F2-16 (cadena de integridad); siempre `null` en F2-15. |

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

## Pendiente explícito (fuera de alcance de F2-15)

- **Integraciones no cableadas todavía**: operaciones privilegiadas de F2-10 (step-up, segregación de
  funciones) y cambios de permisos/roles (F2-07/F2-09) son candidatas obvias a emitir auditoría y hoy no lo
  hacen — cablear esas integraciones queda fuera de esta tarea (alcance mínimo y cohesionado, sección 3.2
  del Plan Maestro) y debería abordarse como parte del gate de salida de fase ("operaciones críticas
  generan auditoría íntegra") antes de cerrar la Fase 2.
- **Almacenamiento persistente real**: la elección definitiva (tabla SQL append-only vs. event store vs.
  otro destino) es una decisión arquitectónica pendiente de un ADR propio — ver sección "Decisiones" del
  reporte de cierre de F2-15.
- **Lectura/consulta administrativa**: es F2-20; `InMemoryAuditWriter.Entries` no es esa API.
- **Redacción de PII**: es F2-19; hasta entonces, es responsabilidad de cada llamador no volcar datos
  sensibles sin redactar en `Metadata`/`Reason`.

## Referencias

- `src/Shared.Infrastructure.Security/Audit/` — implementación (`AuditEntry`, `AuditEntryRequest`,
  `AuditActor`, `AuditResource`, `AuditHashCalculator`, `IAuditWriter`, `InMemoryAuditWriter`,
  `AuditServiceCollectionExtensions`).
- `tests/Shared.Infrastructure.Security.Tests/Audit/` — suite de pruebas (campos críticos completos,
  determinismo/sensibilidad del hash, ausencia de update/delete).
