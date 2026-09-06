# 0016. Firma de lotes de auditoría: HMAC-SHA256 simétrico (no firma asimétrica)

**Estado:** Accepted
**Fecha:** 2026-09-06
**Responsable de aprobación:** Javier León (2026-09-06)

## Contexto

El Plan Maestro (Fase 2, Épica F2-D — Auditoría inmutable, F2-17: "Firma y timestamp — Integrar firma
de lotes o eventos", criterio de aceptación "Verificación independiente") pide cerrar un hueco conocido
y explícitamente documentado de F2-16 (cadena de integridad, `IAuditIntegrityVerifier`): un atacante con
acceso de **escritura** al almacenamiento subyacente de auditoría (pero sin acceso al mecanismo de
firma) puede reconstruir una cadena de hashes alternativa completa y auto-consistente (`AuditHash`/
`PreviousAuditHash` recalculados con el mismo algoritmo público, `AuditHashCalculator`), de forma que
`IAuditIntegrityVerifier.Verify` la reporte como íntegra sin serlo. La cadena de hashes por sí sola no
distingue "esta secuencia es la que realmente ocurrió" de "esta secuencia es internamente consistente".

`IAuditBatchSigner`/`HmacAuditBatchSigner` (`src/Shared.Infrastructure.Security/Audit/`) resuelve esto
firmando lotes de `AuditEntry` con HMAC-SHA256, usando material de clave versionado resuelto vía
`ISecretProvider` (F2-12, ADR 0014) — mismo patrón de resolución de claves que
`AesGcmEncryptionProvider` (F2-13). Esta es una decisión de seguridad con una consecuencia estructural
que condiciona a quién se le puede probar la integridad de la auditoría, no solo un detalle de
implementación: HMAC es un esquema **simétrico** (firmar y verificar requieren el mismo secreto), a
diferencia de una firma asimétrica (par de claves pública/privada), donde cualquiera con la clave
pública puede verificar sin nunca tener acceso al secreto de firma.

La revisión de arquitectura de F2-17 (bitcode-architecture-guardian, 2026-09-06) señaló explícitamente
que esta elección no pasó todavía por el circuito de aprobación humana que la sección 13 del Plan
Maestro reserva para decisiones estructurales de este tipo — el mismo tratamiento que ya recibieron el
proveedor de secretos (ADR 0014) y el contrato de mTLS servicio-a-servicio (ADR 0015) — pese a estar
extensamente razonada en el código y en `docs/guia-auditoria-inmutable.md`.

## Decisión

Se adopta HMAC-SHA256 (vía `ISecretProvider`, sin nueva infraestructura de claves) como mecanismo de
firma de lotes de auditoría de F2-17, con la limitación de alcance documentada explícitamente:

1. **HMAC-SHA256 con clave versionada resuelta por `ISecretProvider`** — no una firma asimétrica
   (RSA/ECDSA). El framework hoy no expone ninguna abstracción de par de claves asimétrico/PKI (solo
   `ISecretProvider` para material simétrico e `IEncryptionProvider`, también simétrico, AES-256-GCM);
   introducir soporte de claves asimétricas sería agregar una pieza de infraestructura criptográfica
   nueva, de alcance mayor que el hueco puntual que F2-17 necesita cerrar.
2. **"Verificación independiente" se interpreta como independencia de PROCESO, no de SECRETO.** El
   firmante y el verificador pueden ser servicios/procesos distintos (por ejemplo, el servicio que
   escribe auditoría al firmar cada lote, y un proceso de cumplimiento/verificación periódico separado
   que solo lee), siempre que ambos compartan acceso al mismo `ISecretProvider` — pero ninguno de los
   dos, por sí solo, es también quien tiene acceso de escritura directa al almacenamiento de auditoría
   subyacente. Esto sí cierra el hueco concreto de F2-16 (reconstrucción de cadena por quien solo tiene
   acceso de escritura al almacenamiento).
3. **Límite explícito y aceptado**: este mecanismo **no sirve** para que un tercero externo sin acceso
   al `ISecretProvider` del framework (por ejemplo, un auditor regulatorio externo, u otro sistema fuera
   del perímetro de confianza) verifique criptográficamente la integridad de un lote de forma
   independiente. Un proyecto consumidor que necesite ese caso de uso concreto requiere una firma
   asimétrica con clave pública — fuera de alcance de F2-17, y debe abordarse como una tarea y un ADR
   nuevos si llegara a necesitarse.

## Alternativas consideradas

- **Firma asimétrica (RSA/ECDSA) desde el inicio.** Descartada para F2-17: requeriría introducir una
  abstracción de PKI/gestión de claves asimétricas que el framework no tiene hoy, de alcance mayor al
  entregable mínimo pedido ("mecanismo aprobado" para firma de lotes) y no justificada todavía por
  ningún caso de uso concreto de verificación por terceros externos. Queda como extensión futura
  explícitamente habilitada por este ADR si un proyecto consumidor la necesita.
- **No firmar nada, confiar solo en la cadena de hashes de F2-16.** Descartada: no cierra el hueco
  conocido y documentado de reconstrucción completa de la cadena por un atacante con acceso de
  escritura al almacenamiento — es exactamente la brecha que F2-17 existe para resolver.
- **Firma por registro individual en vez de por lote.** No evaluada en profundidad: el criterio de
  aceptación del plan pide explícitamente "firma de lotes o eventos", y firmar por lote reduce el costo
  operativo de firmar/verificar frente a un volumen alto de auditoría, sin perder cobertura (alterar
  cualquier registro de un lote invalida la firma del lote completo).

## Consecuencias

- La aprobación humana de la sección 13 sobre esta decisión de seguridad estructural ya fue otorgada
  (Javier León, 2026-09-06): HMAC-SHA256 vía `ISecretProvider` queda aceptado como mecanismo de firma de
  lotes de auditoría para verificación entre procesos internos, con el límite de alcance de este ADR
  explícitamente conocido y aceptado (no cubre verificación por terceros externos sin acceso al
  proveedor de secretos).
- **F2-17 implementado** sobre esta decisión: `src/Shared.Infrastructure.Security/Audit/IAuditBatchSigner.cs`,
  `HmacAuditBatchSigner.cs`, `AuditBatchSignature.cs`, `AuditBatchSigningOptions.cs`,
  `AuditBatchSigningServiceCollectionExtensions.cs`. Ver `docs/guia-auditoria-inmutable.md` (sección
  F2-17) para el detalle de diseño y `docs/politica-criptografica.md` para el algoritmo aprobado.
- Rotar la clave activa de firma no invalida firmas ya emitidas: `AuditBatchSignature.KeyVersion` fija
  la versión usada, y `VerifyAsync` resuelve esa versión específica, no la activa vigente al momento de
  verificar (mismo criterio de rotación que `AesGcmEncryptionProvider`, F2-13).
- F2-17 no define cuándo se firma un lote en producción (política de "cada N registros"/"cada X
  minutos" queda a cargo del proyecto consumidor) ni persiste la firma junto al lote (responsabilidad de
  la implementación real de `IAuditWriter` que un proyecto conecte, o del destino WORM de F2-18).
- Si en el futuro un proyecto consumidor necesita verificación por un tercero externo sin acceso al
  `ISecretProvider` del framework, esa necesidad requiere una tarea y un ADR nuevos (firma asimétrica) —
  este ADR no la resuelve ni la bloquea, solo delimita el alcance actual.

## Riesgos y mitigación

- **Riesgo:** que un proyecto consumidor asuma que la firma HMAC de F2-17 es válida para un escenario de
  verificación por auditor externo sin acceso al proveedor de secretos, y descubra la limitación
  recién en un requisito de cumplimiento real. Mitigación: la limitación está documentada
  explícitamente en el código (`HmacAuditBatchSigner`), en `docs/guia-auditoria-inmutable.md` ("Qué NO
  resuelve F2-17") y en este ADR.
- **Riesgo:** que el `ISecretProvider` y el almacenamiento de auditoría queden comprometidos juntos (por
  ejemplo, el mismo incidente compromete ambos), en cuyo caso la garantía de la firma HMAC se pierde por
  completo (a diferencia de una firma asimétrica, donde comprometer el almacenamiento no compromete la
  clave privada de firma si vive en otro sistema). Mitigación operativa: separar el perímetro de acceso
  al proveedor de secretos del perímetro de acceso de escritura al almacenamiento de auditoría es
  responsabilidad de cada proyecto consumidor — el framework no puede garantizarlo por sí solo con un
  mecanismo simétrico.
- Vinculado al registro de riesgos de F0-12 y a la sección 13 del Plan Maestro.
