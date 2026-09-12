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

### 2.4 Hot-reload real en Kubernetes — ConfigMap montado como volumen (gap cerrado)

`IOptionsMonitor<T>.OnChange`/`ConfigurationFeatureFlagProvider.IsEnabled` reaccionan correctamente a
CUALQUIER proveedor de `IConfiguration` que dispare su change token — esto incluye el caso de referencia
que motivó el diseño (un archivo de configuración montado desde un `ConfigMap` con `reloadOnChange: true`,
comportamiento nativo de `Microsoft.Extensions.Configuration.Json` sin código adicional) y queda verificado
con dos pruebas complementarias, ambas en `tests/Shared.Infrastructure.Security.Tests/FeatureFlags/`:

- `FeatureFlagChangeAuditingServiceTests` — simula la recarga con un `IConfigurationProvider` de prueba
  que dispara `OnReload` a mano (`ReloadableConfigurationSource`, ya que `AddInMemoryCollection` no dispara
  change tokens al modificar un valor — limitación de esa fuente de prueba, no del mecanismo).
- `FeatureFlagsFileHotReloadIntegrationTests` — usa el mecanismo REAL de producción: escribe un archivo
  JSON físico en disco, lo carga con `AddJsonFile(path, optional: true, reloadOnChange: true)` (el mismo
  método que `Program.cs` usa contra el volumen) y lo REESCRIBE mientras el host ya está corriendo,
  confirmando que (a) `IFeatureFlagProvider.IsEnabled` refleja el valor nuevo sin reiniciar el proceso y
  (b) `FeatureFlagChangeAuditingService` audita la transición `old -> new`. Esto reproduce exactamente lo
  que el kubelet hace al resincronizar un volumen de `ConfigMap` — el proceso .NET nunca sabe que corre
  dentro de un pod, solo ve un archivo que cambió en disco.

**Antes de este cierre de gap**, el `ConfigMap` de `Sample.Api` se montaba únicamente como **variables de
entorno** (`envFrom: configMapRef`), que Kubernetes no actualiza en un contenedor ya corriendo — un cambio
de flag requería `kubectl rollout restart` igual que cualquier otra clave de ese ConfigMap, y
`FeatureFlagChangeAuditingService` nunca llegaba a observar la transición (el proceso arrancaba de cero con
el valor nuevo, sin un "valor anterior" en memoria contra el cual comparar).

**Mecanismo actual (gap cerrado) para `Sample.Api`**:

- La sección `FeatureFlags` vive en un `ConfigMap` SEPARADO, `sample-api-featureflags`
  (`k8s/sample-api/base/featureflags-configmap.yaml`), con una única clave `featureflags.json` que
  contiene el documento JSON completo (no el formato plano `Clave__Subclave` de `envFrom`).
- Ese `ConfigMap` se monta como **volumen de archivo** en `deployment.yaml`
  (`volumes: - configMap: { name: sample-api-featureflags, items: [{ key: featureflags.json, path:
  featureflags.json }] }` + `volumeMounts: - { name: featureflags-config, mountPath: /app/config,
  readOnly: true }`) — **sin `subPath`**: un volumen de `ConfigMap` montado con `subPath` NO se actualiza
  cuando el `ConfigMap` cambia (limitación de Kubernetes — el symlink atómico que el kubelet usa para
  propagar la actualización del volumen no aplica a `subPath`), así que montar el directorio completo (con
  `items` acotando qué clave se materializa) es lo que efectivamente habilita el hot-reload.
- `samples/Sample.Api/Program.cs` agrega, ANTES de `AddModules`:
  `builder.Configuration.AddJsonFile(featureFlagsConfigPath, optional: true, reloadOnChange: true)`, con
  `featureFlagsConfigPath` resuelto desde `FeatureFlags:ConfigFilePath` (configurable) o, por defecto,
  `/app/config/featureflags.json` — coincide con el `mountPath` del volumen. `optional: true` es
  obligatorio: en desarrollo local (sin el volumen montado) el archivo no existe y el proyecto debe seguir
  arrancando con los flags en su valor por defecto.
- El resto de la configuración de `Sample.Api` (`ASPNETCORE_ENVIRONMENT`, `Logging`, `OpenTelemetry`, etc.)
  sigue viviendo en `sample-api-config` vía `envFrom` — ese patrón sigue siendo correcto para valores que
  no necesitan cambiar sin reiniciar el proceso; migrar solo `FeatureFlags` a un volumen es el "cambio
  mínimo" necesario para cerrar este gap concreto.

**Latencia de propagación real**: el kubelet resincroniza el contenido de un volumen de `ConfigMap` en un
ciclo periódico (el "sync period" del kubelet, valor por defecto de referencia: hasta ~1 minuto), no de
forma instantánea al hacer `kubectl apply` — un consumidor que necesite una recarga más rápida que ese
piso debe evaluar herramientas externas de reload inmediato (por ejemplo un sidecar que dispare `SIGHUP` o
un webhook), fuera de alcance de este mecanismo.

**`BitCode.Gateway` no registra `AddSharedFeatureFlags` a la fecha de este documento** (no evalúa ningún
flag on/off) — por eso este cierre de gap solo tocó `Sample.Api`/`k8s/sample-api/`. Si un consumidor real
agrega feature flags al Gateway, debe seguir el mismo patrón (`ConfigMap` propio montado como volumen sin
`subPath`, `AddJsonFile(reloadOnChange: true)` en `src/BitCode.Gateway/Program.cs`) descrito arriba.

## 3. Resumen de la frontera Fase 4 vs. Fase 6

| Capacidad | F4-12 (esta tarea) | Feature Management (Fase 6, módulo 4) |
|---|---|---|
| Evaluación on/off | Sí (`IFeatureFlagProvider.IsEnabled`) | Sí, y además con reglas de targeting |
| Segmentación por usuario/tenant | No | Sí |
| Rollout gradual (%) | No | Sí |
| Origen del valor | `IConfiguration` (ConfigMap/appsettings) | Persistencia propia (tabla/servicio dedicado) |
| Endpoint administrativo con actor autenticado | No | Sí |
| Auditoría | Sí, actor de sistema fijo (sección 2.3) | Sí, con actor humano identificado |
