# Gate de salida — Fase 5 (Disaster Recovery y multi-región)

**Fase:** 5 — Disaster Recovery y multi-región ([Plan Maestro de BitCode](plan-maestro-bitcode-ia.md), líneas 598-644).
**Backlog:** F5-01 a F5-13, las 13 tareas, cerradas con evidencia real (ver tabla de commits, sección 1).
**Fecha:** 2026-09-08.
**Estado del gate: aprobado con condiciones.** Tres de los seis ítems del gate quedan en estado
**parcial**, no por trabajo de ingeniería faltante, sino porque su cierre total depende de una decisión
de negocio/infraestructura que el propio Plan Maestro reserva a un humano (sección 13) — el mismo patrón
ya usado para cerrar la Fase 4 (`docs/informe-capacity-tests-f4-14.md`) frente a los puntos que requerían
un clúster Kubernetes real inexistente en este entorno.

---

## 1. Backlog cerrado

| ID | Trabajo | Commit | Estado |
|---|---|---|---|
| F5-01 | Business Impact Analysis | `9e23016` | Implementado (perfiles propuestos, ver §2.1) |
| F5-02 | Ownership regional | `3463246`, `f550f3e` | Implementado |
| F5-03 | Routing regional | `3e669be` | Implementado |
| F5-04 | Replicación SQL | `4e5c7b9` | Implementado (Standard/Gold; Platinum ver §2.3) |
| F5-05 | Replicación Kafka | `44fe944` | Implementado |
| F5-06 | Cache regional | `a12754d` | Implementado |
| F5-07 | Backups | `6cb1371` | Implementado |
| F5-08 | PITR | `5d35078` | Implementado |
| F5-09 | Backups inmutables | `978306b` | Implementado (modo Governance, ver §2.2) |
| F5-10 | Failover automatizado | `0d2666d` | Implementado |
| F5-11 | Failback | `f94c594` | Implementado |
| F5-12 | DR drills | `d91e90c` | Implementado (Standard/Gold; Platinum ver §2.3) |
| F5-13 | Chaos regional | `7552cea` | Implementado |

## 2. Checklist del gate de salida

- [x] **El propietario de escritura está definido.** F5-02 (`RegionalOwnershipBehavior`, determinístico
  y testeado) + F5-03 (routing en el Gateway) son evidencia sólida y sin condiciones.
- [x] **Failover y failback tienen runbooks ejecutables.** Ambos ejecutados con procesos de SO reales
  (F5-10, F5-11); failback además con test automatizado (`RegionFailbackNoSplitBrainIntegrationTests`,
  3 corridas sin flakiness). Son operación manual vía CLI con `--confirm` obligatorio, no un daemon
  desatendido — pero un runbook ejecutable exige pasos reproducibles y verificados, no automatización
  autónoma, y eso sí se cumple.
- [x] **No existe split-brain bajo el modelo aprobado.** `RegionFailbackNoSplitBrainIntegrationTests`
  fuerza el escenario adversarial de escritura concurrente contra ambas regiones durante la ventana de
  failback: 80 observaciones, 3 corridas, sin doble escritor en ninguna.
- [ ] **Cada módulo tiene perfil RPO/RTO.** *Parcial.* Ver §2.1.
- [ ] **Backup y restore fueron probados.** *Parcial.* Ver §2.2.
- [ ] **Los objetivos se demuestran con tiempos medidos.** *Parcial.* Ver §2.3.

### 2.1 Perfiles RPO/RTO — pendiente de recalibración de negocio

`docs/bia-fase5.md` asigna un perfil (Standard/Gold/Platinum) a cada uno de los 18 componentes
clasificados, pero el propio documento se autodeclara **"Propuesto — pendiente de recalibración con
negocio/infraestructura/presupuesto"**, tal como la sección "Perfiles iniciales" del Plan Maestro exige
explícitamente para Platinum ("requiere una decisión explícita sobre replicación síncrona, consenso y
latencia") y de hecho recomienda para los tres perfiles en general.

**Pendiente conocido:** validación de negocio de los perfiles asignados por componente. Es una decisión
de producto/presupuesto, no una brecha de ingeniería — el mecanismo que consume el perfil (F5-02 a F5-13)
ya funciona con cualquier perfil que se le asigne.

### 2.2 Backups inmutables — WORM en modo Governance, no Compliance

F5-09 prueba con evidencia real que un intento de borrado privilegiado sobre un backup protegido falla
(`UnauthorizedAccessException` real en Windows, `BackupImmutabilityFileSystemTests`). Pero el mecanismo
(ACL Deny sobre NTFS local) es **modo "Governance"**: reversible por una acción administrativa separada
(`Unlock-BackupImmutability.ps1` con la identidad correcta), no **"Compliance"** real (Object Lock de un
proveedor cloud, irreversible incluso para un administrador). `docs/backups-inmutables-fase5.md` lo
declara explícitamente, sin maquillarlo.

**Pendiente conocido:** contratación de una cuenta/proveedor cloud con Object Lock real para el modo
Compliance — requiere aprobación humana y presupuesto (sección 13 del Plan Maestro), no existe en este
entorno de desarrollo local.

### 2.3 Tiempos medidos — Standard y Gold demostrados, Platinum fuera de alcance

F5-04, F5-05, F5-10, F5-11 y F5-12 miden RPO/RTO reales de reloj de pared (no estimados) contra procesos
de SO reales, y `docs/dr-drill-fase5.md` corre el simulacro end-to-end dos veces completas sin
flakiness. Esos tiempos satisfacen los perfiles **Standard** (RPO≤15min/RTO≤60min) y **Gold**
(RPO≤60s/RTO≤5min).

El perfil **Platinum** (RPO≈0, RTO≤1min) exige replicación síncrona con consenso — una decisión de
arquitectura que el propio Plan Maestro reserva explícitamente a una decisión de negocio explícita
("Platinum requiere una decisión explícita sobre replicación síncrona, consenso y latencia"). Ningún
componente productivo tiene hoy asignado Platinum de forma aprobada (ver §2.1), así que no hay nada que
demostrar todavía para ese perfil — y el RPO medido en el drill (~2.6s con replicación asíncrona) confirma
que, sin esa decisión, Platinum no es alcanzable con el mecanismo actual.

**Pendiente conocido:** decisión de negocio/arquitectura sobre replicación síncrona para Platinum, antes
de comprometerse contractualmente a ese SLA. No es una tarea de ingeniería adicional dentro del alcance
de F5-01 a F5-13.

## 3. Recomendación

Los tres puntos parciales comparten la misma causa raíz: dependen de una decisión de negocio o de
infraestructura/presupuesto que la sección 13 del Plan Maestro reserva explícitamente a un humano, no de
trabajo de ingeniería pendiente dentro del backlog de la Fase 5. El mecanismo técnico para los tres está
implementado, probado con evidencia real y listo para operar en cuanto esas decisiones se tomen.

**La Fase 5 se da por cerrada** con estos tres pendientes documentados explícitamente (mismo criterio ya
aplicado en el cierre de la Fase 4 frente a los puntos que requerían un clúster Kubernetes real), a
diferencia de fingir un cumplimiento que la evidencia no respalda.
