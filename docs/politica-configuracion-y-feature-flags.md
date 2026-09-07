# Política de configuración y feature flags (F4-12)

Fase 4 (Runtime de alta disponibilidad), tarea F4-12: "Externalizar config y feature flags". Este
documento cubre dos cosas distintas que la fila del backlog agrupa en una sola tarea:

1. Confirmación de que la configuración crítica de `Sample.Api`/`BitCode.Gateway` está 100% externalizada
   (ya lo estaba desde F4-02 en adelante — ver sección 1).
2. Un mecanismo NUEVO de feature flags simples (on/off), acotado deliberadamente a lo que Fase 4 necesita
   a nivel de runtime/infraestructura — ver sección 2 para la frontera explícita con el módulo "Feature
   Management" reservado para Fase 6.

## 1. Qué va en ConfigMap, qué va en Secret, qué va en FeatureFlags

| Tipo de dato | Dónde vive | Ejemplo | Por qué |
|---|---|---|---|
| Configuración no sensible, la misma para todas las instancias de un ambiente | `ConfigMap` (`k8s/*/base/configmap.yaml` + overlays por ambiente) | `OpenTelemetry__OtlpEndpoint`, `RateLimiting__PermitLimit`, `Logging__LogLevel__Default` | No es secreto; cambia por ambiente (dev/staging/prod), no por instancia. Ver `docs/adr/0014-secretos-proveedor-vault-propuesto.md` para la frontera con Secret. |
| Credenciales, cadenas de conexión, claves de firma | `Secret` (`k8s/*/base/secret.example.yaml`, nunca committeado con valores reales) | `ConnectionStrings__Default`, `Jwt__SecretKey`, `Secrets__Vault__Token` | Plan Maestro sección 3.2: "guardar secretos en el repositorio" está prohibido. `ISecretProvider`/`VaultSecretProvider` (F2-12, ADR 0014) es el mecanismo para secretos que un handler de aplicación necesita resolver en tiempo de ejecución (no solo al arrancar el proceso) — un `Secret` de Kubernetes sigue siendo el mecanismo para lo que arranca el proceso (cadena de conexión, clave JWT). |
| Interruptores on/off de una capacidad, evaluados en runtime | Sección `FeatureFlags` de `IConfiguration` (ConfigMap o cualquier otro proveedor) vía `IFeatureFlagProvider` (F4-12) | `FeatureFlags__NuevoFlujoDePagos: "true"` | Ver sección 2. |

Auditoría de F4-12 sobre el estado ya construido en F4-02 a F4-11 (parte 1 de esta tarea, sin cambios de
código): revisados `samples/Sample.Api/appsettings.json`, `src/BitCode.Gateway/appsettings.json` y los
cuatro manifiestos de `k8s/` (`sample-api/base/configmap.yaml`, `sample-api/base/secret.example.yaml`,
`gateway/configmap.yaml`, `gateway/secret.example.yaml`) — ninguno de los dos `appsettings.json`
committeados trae una cadena de conexión, secreto o endpoint hardcodeado (`ConnectionStrings:Default` es
`""` en `Sample.Api`, coherente con ADR 0014; `BitCode.Gateway` no declara `Jwt:SecretKey` en absoluto,
solo `Jwt:Issuer`/`Jwt:Audience`, que no son secretos). No se encontró nada que corregir: la
externalización de F4-02 en adelante ya cumplía el criterio antes de esta tarea. `Sample.Api` no cablea
Redis (`Shared.Infrastructure.Caching`) ni Kafka (`Shared.Infrastructure.Messaging.Kafka`) todavía — no
hay ninguna cadena de conexión de esos dos sistemas que auditar en este proyecto de referencia hasta que
un consumidor real los habilite (en ese momento, sigue el mismo patrón: `Caching__RedisConnectionString`
como valor NO sensible en `ConfigMap` si el proveedor Redis está en la misma red interna sin credencial, o
en `Secret` si expone una cadena con contraseña — criterio ya establecido, no nuevo de esta tarea).

## 2. Feature flags simples (F4-12) — mecanismo y límites deliberados

### 2.1 Qué es

`IFeatureFlagProvider` (`Shared.Infrastructure.Security.FeatureFlags`, junto a `Audit/` — no es casualidad,
ver sección 2.3) evalúa un flag lógico como **on/off**, nada más:

```csharp
public interface IFeatureFlagProvider
{
    bool IsEnabled(string flagName, bool defaultValue = false);
}
```

`services.AddSharedFeatureFlags(configuration)` registra:

- `ConfigurationFeatureFlagProvider` (implementación por defecto de `IFeatureFlagProvider`): lee cada flag
  de la sección `FeatureFlags` de `IConfiguration` vía `IOptionsMonitor<FeatureFlagsOptions>` (nunca
  `IOptions<T>`, que congelaría el valor leído la primera vez) — `FeatureFlagsOptions` hereda de
  `Dictionary<string, bool>` a propósito, para que `FeatureFlags__NombreDelFlag` en el `ConfigMap` mapee
  directo a una entrada del diccionario sin un nivel de anidamiento extra.
- `FeatureFlagChangeAuditingService` (`IHostedService`): audita cada transición de valor (`old -> new`)
  detectada cuando `IOptionsMonitor<FeatureFlagsOptions>.OnChange` se dispara — ver sección 2.4.
- `IAuditWriter` (F2-15 a F2-20, `AddSharedAuditing()` interno si el proyecto todavía no lo llamó) — la
  auditoría de flags reutiliza la infraestructura de auditoría inmutable ya construida en Fase 2 (cadena de
  hash, firma HMAC de lotes, exportación WORM), no un mecanismo de log nuevo.

### 2.2 Qué NO es (frontera explícita con Fase 6)

El Plan Maestro (tabla de módulos de Fase 6, "Plataforma funcional empresarial", orden 4) reserva un
bounded context completo llamado **"Feature Management"** con las capacidades: *"Flags, segmentos, rollout
y auditoría"*. `IFeatureFlagProvider` de F4-12 deliberadamente **no** implementa:

- Segmentación por usuario/tenant/atributo (targeting).
- Porcentaje de rollout gradual (canary de features, no de despliegue — no confundir con F4-13).
- Un endpoint administrativo para cambiar un flag desde una UI/API con actor autenticado.
- Persistencia propia de flags (viven en `IConfiguration`, no en una tabla de base de datos con su propio
  ciclo de vida/versión).

Esto **no** es una limitación técnica temporal: es la frontera de alcance de F4-12 respetada a propósito
(Plan Maestro sección 3.2, prohibición de adelantar/mezclar trabajo de otra fase sin justificación). Un
proyecto consumidor que necesite targeting o rollout gradual antes de que exista el módulo de Fase 6 debe
resolverlo en su propio código de aplicación (por ejemplo, consultando `ITenantContext` y aplicando su
propia regla sobre el resultado de `IsEnabled`), sin extender `IFeatureFlagProvider`.

### 2.3 Auditoría — mecanismo y limitación de actor

Cada vez que `IOptionsMonitor<FeatureFlagsOptions>.OnChange` se dispara, `FeatureFlagChangeAuditingService`
compara el snapshot anterior contra el nuevo (`FeatureFlagChangeAuditingService.ComputeChanges`, `internal`,
cubierto por prueba unitaria directa) y por cada flag cuyo valor cambió (agregado, removido o modificado)
escribe una `AuditEntry` vía `IAuditWriter.WriteAsync`:

- `Action`: `"featureflags.changed"` (`FeatureFlagChangeAuditingService.AuditAction`).
- `Resource`: `Type = "feature-flag"`, `Id = {nombre del flag}`.
- `Actor`: **siempre** `AuditActorType.System` con id fijo `"system.featureflags.config-reload"`
  (`FeatureFlagChangeAuditingService.SystemActorId`).
- `Metadata`: `oldValue`/`newValue` (cadenas `"True"`/`"False"`, o `null` si el flag se agregó/removió).

**Limitación deliberada, no un defecto pendiente**: cuando el flag se externaliza vía un `ConfigMap`
(patrón F4-02), el cambio de VALOR se conoce (qué flag, de qué a qué, cuándo), pero el proceso .NET nunca
sabe QUIÉN lo originó — esa identidad (usuario de un pipeline de CI/CD, operador que corrió `kubectl
apply`) vive fuera del proceso, en el sistema de control de acceso de Kubernetes/GitOps, no en algo que
`IOptionsMonitor`/`IConfiguration` expongan. Un flujo con auditoría de actor humano identificado requeriría
un endpoint administrativo propio que reciba el cambio ya autenticado — eso es superficie del módulo
"Feature Management" de Fase 6 (targeting/gestión con UI), fuera de alcance de F4-12 a propósito (mismo
principio que la sección 2.2).

### 2.4 Límite real del hot-reload con el ConfigMap actual

`IOptionsMonitor<T>.OnChange`/`ConfigurationFeatureFlagProvider.IsEnabled` reaccionan correctamente a
CUALQUIER proveedor de `IConfiguration` que dispare su change token — esto incluye el caso de referencia
que motivó el diseño (un archivo de configuración montado desde un `ConfigMap` con `reloadOnChange: true`,
comportamiento nativo de `Microsoft.Extensions.Configuration` sin código adicional) y quedó verificado con
un proveedor de configuración de prueba que dispara el change token explícitamente (`ReloadableConfigurationSource`
en `tests/Shared.Infrastructure.Security.Tests/FeatureFlags/`, ya que `AddInMemoryCollection` no dispara
change tokens al modificar un valor — limitación de esa fuente de prueba, no del mecanismo).

**Sin embargo**, el `ConfigMap` real de `Sample.Api`/`BitCode.Gateway` (`k8s/*/base/configmap.yaml`,
patrón fijado desde F4-02) se monta como **variables de entorno** (`envFrom: configMapRef`, ver
`deployment.yaml` de cada uno) — Kubernetes no actualiza el entorno de un contenedor ya corriendo cuando el
`ConfigMap` cambia. Con este patrón de despliegue concreto, un cambio de `FeatureFlags__NombreDelFlag`
requiere `kubectl rollout restart` (o un pipeline que lo dispare) igual que cualquier otra clave existente
de ese `ConfigMap` — **no hay recarga en caliente hoy**, y por lo tanto `FeatureFlagChangeAuditingService`
tampoco llega a observar la transición (el proceso arranca de cero con el valor nuevo, sin un "valor
anterior" en memoria contra el cual comparar).

Esto es una limitación real y documentada del despliegue actual, no del mecanismo: un proyecto que necesite
recarga en caliente verdadera (y, con ella, auditoría de la transición dentro del mismo proceso) debe montar
la sección `FeatureFlags` como un archivo (`ConfigMap` como volumen, `appsettings.featureflags.json` con
`reloadOnChange: true` en `Program.cs`) en vez de variables de entorno — cambio de infraestructura de
despliegue que queda fuera de alcance de F4-12 (afectaría `deployment.yaml`/`Program.cs` más allá del
"cambio mínimo" de esta tarea) y debe evaluarse como una tarea siguiente si un consumidor real lo necesita.

## 3. Resumen de la frontera Fase 4 vs. Fase 6

| Capacidad | F4-12 (esta tarea) | Feature Management (Fase 6, módulo 4) |
|---|---|---|
| Evaluación on/off | Sí (`IFeatureFlagProvider.IsEnabled`) | Sí, y además con reglas de targeting |
| Segmentación por usuario/tenant | No | Sí |
| Rollout gradual (%) | No | Sí |
| Origen del valor | `IConfiguration` (ConfigMap/appsettings) | Persistencia propia (tabla/servicio dedicado) |
| Endpoint administrativo con actor autenticado | No | Sí |
| Auditoría | Sí, actor de sistema fijo (sección 2.3) | Sí, con actor humano identificado |
