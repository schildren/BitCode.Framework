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
- **Redacción de PII**: es F2-19; hasta entonces, es responsabilidad de cada llamador no volcar datos
  sensibles sin redactar en `Metadata`/`Reason` — la metadata que este cierre agrega (`abacDecisionReason`,
  `permission`, `roleName`) son identificadores/códigos de negocio, no PII.

## Referencias

- `src/Shared.Infrastructure.Security/Audit/` — implementación (`AuditEntry`, `AuditEntryRequest`,
  `AuditActor`, `AuditResource`, `AuditHashCalculator`, `IAuditWriter`, `InMemoryAuditWriter`,
  `AuditServiceCollectionExtensions`).
- `src/Shared.Infrastructure.Security/PrivilegedOperations/AuditingAuthorizationPolicyEvaluator.cs` —
  cableado de auditoría sobre step-up/segregación de funciones (F2-10).
- `src/Shared.Infrastructure.Security/Permissions/RoleManagerPermissionExtensions.cs` — cableado de
  auditoría sobre alta/baja de permiso de un rol (F2-07/F2-09).
- `tests/Shared.Infrastructure.Security.Tests/Audit/` — suite de pruebas (campos críticos completos,
  determinismo/sensibilidad del hash, ausencia de update/delete).
- `tests/Shared.Infrastructure.Security.Tests/PrivilegedOperations/AuditingAuthorizationPolicyEvaluatorTests.cs`
  y `PrivilegedOperationsEndToEndTests.cs` — suite de pruebas del cableado de F2-10.
- `tests/Shared.Infrastructure.Security.Tests/RoleManagerPermissionExtensionsTests.cs` — suite de pruebas
  del cableado de F2-07/F2-09.
