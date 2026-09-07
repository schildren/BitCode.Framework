# Manifiestos de despliegue Kubernetes — BitCode.Framework

**Tarea:** F4-02 (Fase 4 — Runtime de alta disponibilidad) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md);
actualizado por F4-04 (probes), F4-05 (shutdown graceful) y F4-06 (HPA).
**Fecha:** 2026-09-07
**Estado:** Aplicado. El criterio de aceptación de la fila F4-02 es "Configuración validada": no hay un
clúster Kubernetes real disponible en este entorno, así que la validación se hizo con herramientas
estáticas reales contra los manifiestos generados (evidencia en la sección 5) — igual que F4-01 dejó
"escaneo sin CVE crítica" confirmado localmente y pendiente de la primera corrida en CI real. F4-05
("Sin requests o mensajes perdidos") se documenta en la sección 5-ter. F4-06 ("Escala bajo carga") se
documenta en la sección 5-quater — la política HPA queda declarada y validada sintácticamente, pero el
criterio literal de "escala bajo carga" en ejecución real queda para F4-14 (Capacity tests), que sí
requiere un clúster con metrics-server.

---

## 1. Decisión: Kustomize (base + overlays), no Helm

El backlog permite elegir Helm o Kustomize. Se eligió **Kustomize**:

- **Sin plantillas propietarias.** Kustomize opera sobre YAML de Kubernetes válido (base + patches
  declarativos), no un lenguaje de templating propio (`{{ }}` de Go templates de Helm). Para el alcance
  actual — un único Deployment/Service/ConfigMap parametrizado por 3 ambientes — eso alcanza sin
  necesitar `values.yaml`, `_helpers.tpl` ni un motor de render adicional.
- **Soporte nativo en `kubectl`.** `kubectl kustomize`/`kubectl apply -k` están integrados en `kubectl`
  desde 1.14 sin instalar nada adicional (verificado en este entorno: `kubectl` 1.34.1 trae Kustomize
  v5.7.1 embebido). Helm requiere instalar un binario/CLI separado que no está disponible en este
  entorno ni se asume disponible en todos los pipelines de CI/CD de un consumidor del framework.
- **Consistente con el resto del repositorio.** BitCode.Framework no adopta dependencias de
  herramientas externas salvo que agreguen una capacidad que Kubernetes/`kubectl` no cubra ya (mismo
  criterio que descartó SDKs propietarios en `ISecretProvider`/`IOidcAdapter` — ADR 0014/0004). Kustomize
  cumple el requisito de "paquete de despliegue por ambiente" sin esa dependencia nueva.

**Cuándo reconsiderar Helm:** si en una tarea futura aparecen necesidades que Kustomize no cubre bien
— lógica condicional compleja entre valores, un chart público a publicar en un repositorio Helm, o
gestión de releases con `helm rollback`/versionado de releases — vale abrir un ADR dedicado. Con el
alcance actual (un host, 3 ambientes, sin necesidad de distribuir el paquete a terceros) no se justifica
esa complejidad adicional. Esta decisión no requiere aprobación humana de la sección 13 del Plan Maestro
(no es elección de IdP/KMS/licencia/broker/base de datos ni ninguna de las categorías listadas ahí).

---

## 2. Estructura del paquete

```
k8s/sample-api/
├── base/
│   ├── kustomization.yaml
│   ├── deployment.yaml       # 1 réplica base (piso), probes F4-04, shutdown F4-05, TODO explícito para F4-07
│   ├── service.yaml          # ClusterIP:80 -> containerPort 8080 (http)
│   ├── configmap.yaml        # Config no sensible (ASPNETCORE_ENVIRONMENT, logging, Secrets:Provider)
│   ├── hpa.yaml               # HorizontalPodAutoscaler F4-06, CPU 70% base + TODO custom metrics
│   └── secret.example.yaml   # PLANTILLA documental, no se aplica ni se referencia desde kustomization.yaml
└── overlays/
    ├── dev/
    │   ├── kustomization.yaml   # namespace bitcode-sample-api-dev, tag "dev"
    │   ├── namespace.yaml
    │   ├── configmap-patch.yaml # ASPNETCORE_ENVIRONMENT=Development, logging Debug
    │   ├── deployment-patch.yaml # replicas: 1, requests/limits reducidos
    │   └── hpa-patch.yaml        # min 1 / max 3, CPU 75%
    ├── staging/
    │   ├── kustomization.yaml   # namespace bitcode-sample-api-staging, tag "0.1.0" (inmutable)
    │   ├── namespace.yaml
    │   ├── configmap-patch.yaml # ASPNETCORE_ENVIRONMENT=Staging
    │   ├── deployment-patch.yaml # replicas: 2, requests/limits intermedios
    │   └── hpa-patch.yaml        # min 2 / max 6, CPU 70%
    └── prod/
        ├── kustomization.yaml   # namespace bitcode-sample-api-prod, tag "0.1.0" (inmutable)
        ├── namespace.yaml
        ├── configmap-patch.yaml # ASPNETCORE_ENVIRONMENT=Production, Secrets:Provider=Vault
        ├── deployment-patch.yaml # replicas: 3 (piso mínimo, tolera pérdida de 1 nodo), requests/limits altos
        └── hpa-patch.yaml        # min 3 / max 10, CPU 65%
```

Cada ambiente se despliega con:

```powershell
kubectl apply -k k8s/sample-api/overlays/dev      # o staging / prod
```

`kubectl kustomize k8s/sample-api/overlays/<env>` (sin `apply`) permite inspeccionar el YAML final antes
de aplicarlo — es lo que se usó para la validación de la sección 5.

---

## 3. Qué cubre el paquete y qué queda fuera deliberadamente

**Cubierto (alcance de F4-02):**

- `Deployment` con imagen `bitcode/sample-api` (F4-01), puerto `8080`, `securityContext` non-root
  explícito consistente con la imagen chiseled (`docs/politica-contenedores.md` sección 3.4),
  `resources.requests`/`limits` parametrizados por ambiente.
- `Service` `ClusterIP` (el tráfico externo entra por el Gateway YARP de F4-08, no directo al Service).
- `ConfigMap` con configuración no sensible, parametrizada por overlay (`ASPNETCORE_ENVIRONMENT`, nivel
  de logging, `Secrets:Provider`).
- Referencia a un `Secret` externo (`sample-api-secrets`, vía `envFrom.secretRef`) para las claves
  sensibles (`ConnectionStrings__Default`, `Secrets__Vault__Token` si aplica) — **nunca un valor real
  committeado**; `base/secret.example.yaml` documenta las claves esperadas como plantilla, no como
  recurso aplicable (Plan Maestro sección 3.2, prohibido guardar secretos en el repositorio; mismo
  patrón que `docs/guia-secret-provider.md`/ADR 0014).
- `Namespace` por ambiente (`bitcode-sample-api-{dev,staging,prod}`), aislamiento básico entre
  ambientes.
- Tag de imagen inmutable en staging/prod (`0.1.0`, no `latest`), mutable solo en dev (`dev`) — mismo
  principio de reproducibilidad que `docs/politica-contenedores.md` sección 3.3 aplica a las imágenes
  base del Containerfile.
- `startupProbe`/`livenessProbe`/`readinessProbe` (F4-04) contra `/health/live` y `/health/ready`
  (F1-25, `Shared.Infrastructure.Web` — ver `docs/guia-health-checks.md`). `livenessProbe` y
  `startupProbe` apuntan a `/health/live`, que nunca depende de SQL Server/Redis (mismo criterio que
  la guía de F1-25: un `restart` no arregla una dependencia externa caída, así que el proceso nunca se
  reinicia por eso — criterio de aceptación de F4-04, "sin bucles de reinicio por dependencia no
  crítica"). `readinessProbe` apunta a `/health/ready`, que sí refleja SQL Server (y Redis si está
  configurado): si falla, el pod sale del `Service` sin reiniciarse. Ver la sección 5 (evidencia F4-04)
  para la prueba real con el contenedor de F4-01 y SQL Server arriba/abajo.
- `lifecycle.preStop` (F4-05) con la acción `sleep` NATIVA del kubelet (`seconds: 10`, estable desde
  Kubernetes 1.29) y `terminationGracePeriodSeconds: 40` a nivel de Pod — drena el tráfico antes de que
  el proceso reciba `SIGTERM`, sin depender de un shell/binario dentro del contenedor (inviable en la
  imagen "chiseled", ver sección 5-ter). Deliberadamente NO se usa `lifecycle.preStop.exec` (requeriría
  `/bin/sh` o un binario `sleep`, ninguno presente en la imagen chiseled — mismo motivo por el que los
  tres probes de arriba son `httpGet`, nunca `exec`).

**Agregado por F4-06 (ver sección 5-quater para el detalle completo):**

- **HPA** (`HorizontalPodAutoscaler`, `base/hpa.yaml` + `overlays/*/hpa-patch.yaml`) — escala por CPU
  (`resources.requests.cpu`, ya definido desde F4-02) entre el piso `replicas` fijo de cada overlay
  (1/2/3) y un techo parametrizado por ambiente (3/6/10). La métrica de aplicación queda como `TODO`
  documentado dentro de `hpa.yaml` (requiere Prometheus Adapter + `custom.metrics.k8s.io`, no instalado
  en este entorno) — no se agrega un metric type `Pods`/`External` sin esa API real porque rompería el
  HPA completo, no solo esa métrica.

**Deliberadamente fuera de alcance (tareas futuras del backlog, marcadas con `TODO(F4-0N)` en
`deployment.yaml`), para no mezclar el alcance de tareas distintas (Plan Maestro sección 3.2):**

- **PodDisruptionBudget y `topologySpreadConstraints`** — F4-07.
- **Gateway YARP / Ingress** — F4-08. El `Service` es `ClusterIP`, sin exposición externa todavía.

---

## 4. Configuración vs. secretos

Sigue el mismo criterio que `docs/guia-secret-provider.md`/ADR 0014 ya establece a nivel de aplicación:

- **`ConfigMap`** (`sample-api-config`): valores no sensibles, versionados en el repositorio sin riesgo
  (nivel de log, `ASPNETCORE_ENVIRONMENT`, qué `ISecretProvider` usar).
- **`Secret`** (`sample-api-secrets`, referenciado pero no definido acá): cadenas de conexión,
  `Secrets:Vault:Token`. Se provisiona por un mecanismo externo al repositorio en cada clúster/ambiente
  — un operador de sincronización (p. ej. External Secrets Operator contra Vault, ADR 0014) o el propio
  pipeline de despliegue con acceso al secret store real. Este repositorio nunca contiene ese Secret con
  valores reales; `k8s/sample-api/base/secret.example.yaml` es una plantilla comentada, deliberadamente
  no incluida en ningún `kustomization.yaml` (no se aplica).

---

## 5. Evidencia de validación (F4-02, ejecutada en este entorno)

No hay clúster Kubernetes real disponible en este entorno (sin acceso a un `kubectl` con contexto
apuntando a un clúster real), así que "Configuración validada" se demuestra con herramientas estáticas
reales, no simuladas, contra el YAML generado:

1. **`kubectl kustomize` (build real, Kustomize v5.7.1 embebido en `kubectl` 1.34.1) para los 3
   overlays** — cada uno resuelve sin error ni warning:
   ```
   kubectl kustomize k8s/sample-api/overlays/dev
   kubectl kustomize k8s/sample-api/overlays/staging
   kubectl kustomize k8s/sample-api/overlays/prod
   ```
   Confirma que el `base` + los patches de cada overlay (merge estratégico de `Deployment`/`ConfigMap`,
   `images.newTag`, `namespace`, `labels`) son válidos y se fusionan sin conflicto — el merge estratégico
   de Kustomize ya requiere conocer el esquema real de `apps/v1 Deployment`/`v1 ConfigMap` para
   fusionar correctamente (p. ej. `containers` por `name` como clave de merge list), no es solo
   concatenación de texto.

2. **`kubeconform` v0.8.0 (`yannh/kubeconform`, descargado y ejecutado localmente contra los binarios
   de release oficiales, Apache-2.0) en modo `-strict` contra el YAML final de cada overlay, validado
   contra el esquema real de Kubernetes 1.30**:
   ```
   kubeconform -strict -summary -kubernetes-version 1.30.0 rendered_dev.yaml
   kubeconform -strict -summary -kubernetes-version 1.30.0 rendered_staging.yaml
   kubeconform -strict -summary -kubernetes-version 1.30.0 rendered_prod.yaml
   ```
   Resultado en los tres casos: `Valid: 4, Invalid: 0, Errors: 0, Skipped: 0` (los 4 recursos por
   ambiente: `Namespace`, `ConfigMap`, `Service`, `Deployment`). Modo `-strict` además rechaza campos
   desconocidos que no formen parte del esquema oficial (protección adicional contra typos de
   estructura, no solo YAML sintácticamente válido). `base/secret.example.yaml` (la plantilla) también
   se validó por separado (`Valid: 1`) aunque no forma parte de ningún `kustomization.yaml` aplicable.

3. **`kubectl apply --dry-run=client`** se intentó pero **no pudo completarse en este entorno**: incluso
   en modo `--dry-run=client`, esta versión de `kubectl` (1.34.1) requiere descubrir la lista de grupos
   de API contra un servidor real (`localhost:8080` por defecto, sin `kubeconfig` configurado en este
   entorno) para resolver el `RESTMapper` — no hay una ruta 100% offline en esta versión. Esta limitación
   queda documentada explícitamente: la validación real de admisión contra un API server (incluidos
   *admission webhooks*, cuotas de namespace, RBAC) solo puede confirmarse contra un clúster real y
   queda pendiente para cuando exista uno disponible (mismo tratamiento que F4-01 dejó pendiente el
   primer `container-scan` real en GitHub Actions).

**Conclusión de la sección 5:** "Configuración validada" se cumple con evidencia real de dos
herramientas independientes (`kubectl kustomize` para el build/merge, `kubeconform -strict` para
conformidad de esquema contra Kubernetes 1.30) — la validación de admisión contra un clúster real queda
como brecha explícita, no oculta.

---

## 5-bis. Evidencia de validación (F4-04, probes)

El criterio de aceptación de F4-04 es "Sin bucles de reinicio por dependencia no crítica". Se validó en
dos niveles:

1. **`kubectl kustomize` sobre los 3 overlays** confirma que `startupProbe`/`livenessProbe`/
   `readinessProbe` quedan en el YAML final con `port: http` resuelto contra `containerPort: 8080` del
   contenedor `sample-api` en los tres ambientes (dev/staging/prod).
2. **Contenedor real (imagen de F4-01) + SQL Server real (contenedor `mcr.microsoft.com/mssql/server`,
   sin Testcontainers)**, sin clúster Kubernetes disponible en este entorno (misma limitación que F4-02,
   sección 5, punto 3) — se ejecutó manualmente la secuencia que un `kubelet` reproduce contra los tres
   endpoints:
   - Con SQL Server disponible: `/health/live` → `200`, `/health/ready` → `200` (`Healthy`).
   - Con SQL Server detenido (`docker stop`, simulando una dependencia crítica caída):
     `/health/live` → sigue en `200` (el proceso .NET nunca dejó de responder — un `kubelet` real NO
     reiniciaría el pod), `/health/ready` → `503` (`Unhealthy`) — un `kubelet` real sacaría el pod del
     `Service` sin reiniciarlo, exactamente la semántica que exige el criterio de aceptación.

   Esto confirma en tiempo de ejecución (no solo por inspección del código) que `livenessProbe`/
   `startupProbe` (apuntan a `/health/live`) nunca dependen de SQL Server/Redis, y que solo
   `readinessProbe` (`/health/ready`) lo hace — sin necesidad de un clúster real, porque la superficie
   que Kubernetes evalúa (el código de estado HTTP de cada endpoint) es exactamente la que se probó.

---

## 5-ter. F4-05 (Shutdown): drenar tráfico y terminar jobs/consumers correctamente

El criterio de aceptación de F4-05 es "Sin requests o mensajes perdidos". El apagado prolijo de un Pod
tiene dos partes independientes que este `deployment.yaml` cubre juntas:

### a) A nivel de manifiesto (esta tarea)

1. **`lifecycle.preStop.sleep.seconds: 10`** — cuando Kubernetes marca el Pod como `Terminating`, dos
   cosas ocurren EN PARALELO: (i) el controlador de `Endpoints`/`EndpointSlice` empieza a sacar el Pod
   del `Service` (propagación no instantánea a kube-proxy/Ingress/balanceador) y (ii) el kubelet ejecuta
   el hook `preStop` del contenedor. Sin este hook, el kubelet enviaría `SIGTERM` al proceso ANTES de que
   (i) termine de propagarse, con la ventana real de que un balanceador siga enrutando requests nuevos a
   un proceso que ya dejó de escuchar. El hook retrasa el envío de `SIGTERM` 10s, dando margen a que esa
   propagación ya haya ocurrido en la inmensa mayoría de los clústeres reales.
   - Se usa la acción `sleep` NATIVA del kubelet (`lifecycle.preStop.sleep`, estable desde Kubernetes
     1.29 — ver [Container Lifecycle Hooks](https://kubernetes.io/docs/concepts/containers/container-lifecycle-hooks/)),
     no `lifecycle.preStop.exec` con un comando de shell: la imagen "chiseled" (F4-01,
     `politica-contenedores.md` sección 3.2) no tiene `/bin/sh` ni ningún binario de coreutils, así que
     un `exec: ["sleep", "10"]` fallaría exactamente igual que un probe `exec` (mismo motivo documentado
     en la sección 3 de este archivo para los tres probes `httpGet` de F4-04). La acción `sleep` la
     ejecuta el kubelet por su cuenta, sin invocar nada dentro del contenedor — compatible con la imagen
     distroless sin ningún cambio a la imagen misma.
2. **`terminationGracePeriodSeconds: 40`** (nivel Pod) — presupuesto total entre que el kubelet marca el
   Pod `Terminating` y el `SIGKILL` forzado si el proceso no terminó solo: los 10s de `preStop` más un
   margen real para que ASP.NET Core drene requests HTTP en vuelo y cualquier `BackgroundService`
   (Outbox, consumer de Kafka, Quartz) termine la unidad de trabajo en curso tras recibir `SIGTERM`. Ver
   el comentario en `deployment.yaml` (nivel `spec.template.spec`) para el desglose completo del
   presupuesto y su relación con `HostOptions.ShutdownTimeout` (punto c).

Validado igual que F4-02/F4-04 (sin clúster real disponible en este entorno): `kubectl kustomize` sobre
los 3 overlays confirma `lifecycle.preStop.sleep.seconds: 10` y `terminationGracePeriodSeconds: 40` en
el YAML final, y `kubeconform -strict -kubernetes-version 1.30.0` valida los 3 overlays renderizados sin
error (`Valid: 4, Invalid: 0, Errors: 0, Skipped: 0` cada uno, incluida la acción `sleep` del hook —
soportada por el esquema oficial de 1.30 sin necesitar `-ignore-missing-schemas`).

### b) A nivel de código (verificado, sin cambios — Fase 1 y Fase 3 ya lo resolvían)

Inspeccionado antes de tocar nada (Plan Maestro sección 3.2, "descubrimiento antes de implementar"):

- **`OutboxPublisherBackgroundService`** (`Shared.Infrastructure.Persistence/Outbox`, F3-03): el loop
  respeta `stoppingToken` en los dos únicos puntos de espera (`Task.Delay(options.PollingInterval,
  stoppingToken)` entre ciclos, y `ThrowIfCancellationRequested()` al inicio de cada fila dentro de
  `OutboxBatchProcessor.ProcessBatchAsync`) — nunca hay un `while(true)` ni un `Task.Delay` sin token. El
  `SaveChangesAsync` que marca cada `OutboxMessage.ProcessedAtUtc` ocurre INMEDIATAMENTE después de
  publicar esa fila (no al final del lote), así que un `SIGTERM` a mitad de un lote deja como máximo una
  fila sin marcar (se reintenta en el próximo arranque — duplicado aceptable, at-least-once ya
  documentado) y nunca un lote entero perdido.
- **`AddSharedBackgroundJobs`** (`Shared.Infrastructure.BackgroundJobs`, F1): ya registra
  `AddQuartzHostedService(options => options.WaitForJobsToComplete = true)` — Quartz espera a que los
  jobs en ejecución terminen antes de que `QuartzHostedService.StopAsync` retorne.
- **`KafkaEventConsumer<TEvent>.ConsumeAndHandleOnceAsync`** (`Shared.Infrastructure.Messaging.Kafka`,
  F3-02/F3-04): recibe y propaga `cancellationToken` en cada punto de espera relevante (el `Task.Run`
  que envuelve `_consumer.Consume(timeout)`, el `IInboxMessageProcessor.ProcessAsync`, el
  `Task.Delay` del backoff de F3-07). El offset solo se confirma DESPUÉS de que el mensaje en curso
  terminó de procesarse (éxito, duplicado descartado, o aislado a DLQ) — nunca a mitad de un mensaje. No
  existe un `IHostedService` compartido que hospede este loop (decisión de diseño de F3-04, documentada
  en `docs/guia-inbox-consumer.md`: cada evento/tópico necesita su propio consumer/group, así que el
  proyecto consumidor decide cómo y cuándo correr el loop) — el patrón de referencia documentado en esa
  guía y ejercitado por `samples/Sample.Eventing.Tests/EndToEndEventingReferenceTests.cs` (F3-13) ya usa
  `while (!stoppingToken.IsCancellationRequested) { await consumer.ConsumeAndHandleOnceAsync(timeout,
  stoppingToken); }`, que respeta la cancelación correctamente.

### c) Brecha real identificada (no cerrada por diseño — documentada, no oculta)

`Microsoft.Extensions.Hosting.HostOptions.ShutdownTimeout` (5s por defecto del host genérico de .NET) es
el presupuesto real que acota CUÁNTO esperan `IHostedService.StopAsync` (incluido
`OutboxPublisherBackgroundService`, `QuartzHostedService`, y cualquier `BackgroundService` que hospede un
`KafkaEventConsumer<TEvent>`) antes de que el host fuerce el apagado. Ningún proyecto de este repositorio
lo configura explícitamente hoy — `samples/Sample.Api` (el proyecto que este `deployment.yaml` despliega)
no registra ni Outbox, ni Quartz, ni ningún `KafkaEventConsumer<TEvent>`, así que el default de 5s nunca
llega a competir con trabajo en segundo plano real en el pilotaje actual, y los `Program.cs` que sí
registren esos componentes están fuera del alcance de F4-05 (viven en `samples/`/proyectos consumidores
futuros, no en `Shared.*`). No se fuerza un valor global mayor en las extensiones de DI compartidas
(`AddSharedOutboxPublisher`/`AddSharedBackgroundJobs`) porque `HostOptions` es única por proceso: un valor
"seguro" para un job de 2 minutos sería un default sorpresivo e injustificado para un proyecto que nunca
registra jobs largos. Queda documentado como responsabilidad explícita del proyecto consumidor (ver el
comentario en `deployment.yaml` y el punto (a) de esta sección): coordinar
`builder.Host.ConfigureHostOptions(o => o.ShutdownTimeout = ...)` con `terminationGracePeriodSeconds`
menos el `preStop.sleep`, para el caso real en que SÍ registre trabajo en segundo plano de vida más larga
que el default.

---

## 5-quater. F4-06 (HPA): escalar por CPU y métricas de aplicación

El criterio de aceptación de F4-06 es "Escala bajo carga". Se separan explícitamente dos partes, para no
declarar cumplido lo que no se pudo comprobar (Plan Maestro sección 3.6, "no marcar Completada con una
validación fallida"):

### a) Política declarada (esta tarea, F4-06)

`base/hpa.yaml` (`autoscaling/v2 HorizontalPodAutoscaler`, `scaleTargetRef` -> `Deployment/sample-api`)
más un `hpa-patch.yaml` por overlay (mismo patrón que `deployment-patch.yaml`):

| Ambiente | minReplicas | maxReplicas | Target CPU (`averageUtilization`) |
|---|---|---|---|
| dev | 1 | 3 | 75% |
| staging | 2 | 6 | 70% |
| prod | 3 | 10 | 65% |

- **CPU (implementado):** único metric type declarado, `type: Resource` / `resource.name: cpu` /
  `target.type: Utilization`. Requiere `resources.requests.cpu` en el contenedor — ya definido en
  `deployment.yaml` y en cada overlay desde F4-02, sin cambios adicionales necesarios en esta tarea.
  `minReplicas` de cada ambiente coincide exactamente con el `replicas` fijo que ya traía
  `deployment-patch.yaml` (1/2/3) — el HPA lo toma como piso, no lo reemplaza (mismo comentario que ya
  dejaba `deployment-patch.yaml` antes de esta tarea, ahora hecho realidad). `maxReplicas`/el target de
  CPU se escalonan por ambiente: prod escala antes (65%, más margen de reacción bajo tráfico real) y
  tolera más réplicas (10) que dev (75%, 3) — perfil de carga esperado distinto por ambiente, mismo
  criterio que ya diferenciaba `resources.requests`/`limits` por overlay.
- **`behavior.scaleDown.stabilizationWindowSeconds: 300`** (los tres ambientes, heredado de
  `base/hpa.yaml`): evita bajar réplicas ante una caída de CPU de corta duración ("flapping"). `scaleUp`
  no se sobreescribe — se mantiene el default de Kubernetes (reacciona sin demora), para no introducir
  latencia extra en el caso que sí importa bajo carga real.
- **Métrica de aplicación (`TODO`, documentado dentro de `hpa.yaml`, NO implementado en esta tarea):**
  agregar un metric type `Pods`/`External` (API `custom.metrics.k8s.io`/`external.metrics.k8s.io`)
  requiere dos piezas que hoy no existen en este repositorio ni en este entorno — (1) un exportador
  Prometheus real: `AddSharedObservability` (F3-10, `Shared.Infrastructure.Observability`) hoy solo
  registra `metrics.AddOtlpExporter(...)` (protocolo OTLP hacia un collector), ningún proyecto
  `Shared.*` ni `samples/Sample.Api` referencia `OpenTelemetry.Exporter.Prometheus.AspNetCore` ni expone
  un endpoint `/metrics` en formato texto Prometheus; (2) Prometheus Adapter (o el futuro OTel Collector
  con exportador Prometheus de F4-10) desplegado en el clúster, sirviendo esa API — no instalado en este
  entorno. Agregar el metric type sin que la API exista rompería el HPA completo (el controller falla
  `GetMetrics` para TODOS los metric types configurados, no solo el nuevo), así que se deja documentado
  como TODO explícito en `hpa.yaml` en vez de "preparado pero inactivo". Ver el comentario completo (con
  los 3 pasos concretos para habilitarlo) directamente en `base/hpa.yaml`.

### b) Validación realizada en este entorno (sin clúster real disponible, misma limitación que F4-02/F4-04/F4-05)

1. **`kubectl kustomize` sobre los 3 overlays** confirma que el `HorizontalPodAutoscaler` se fusiona
   (`base/hpa.yaml` + `overlays/<env>/hpa-patch.yaml`) sin error, con los valores de `minReplicas`/
   `maxReplicas`/`averageUtilization` de la tabla de arriba, y queda en el namespace correcto de cada
   ambiente.
2. **`kubeconform` v0.8.0 en modo `-strict` contra el YAML final de cada overlay, validado contra el
   esquema real de Kubernetes 1.30**:
   ```
   kubeconform -strict -summary -kubernetes-version 1.30.0 rendered_dev.yaml
   kubeconform -strict -summary -kubernetes-version 1.30.0 rendered_staging.yaml
   kubeconform -strict -summary -kubernetes-version 1.30.0 rendered_prod.yaml
   ```
   Resultado en los tres casos: `Valid: 5, Invalid: 0, Errors: 0, Skipped: 0` (los 5 recursos por
   ambiente: `Namespace`, `ConfigMap`, `Service`, `Deployment`, `HorizontalPodAutoscaler`) — confirma que
   `autoscaling/v2` con `behavior.scaleDown` es sintácticamente válido contra el esquema oficial de 1.30.

### c) Lo que NO se comprobó en esta tarea — explícitamente, no oculto

**"Escala bajo carga" (el criterio de aceptación literal) no se comprobó en ejecución real** — no hay
clúster Kubernetes disponible en este entorno, y aunque lo hubiera, el HPA depende de `metrics-server`
(no instalado/aprobado en este entorno) para siquiera calcular `averageUtilization`. Confirmar que el
HPA efectivamente sube/baja réplicas bajo una carga real generada (p. ej. `docs/perf/k6-smoke.js` u otro
script de carga sostenida) es explícitamente el alcance de **F4-14 (Capacity tests)** — esta tarea no lo
simula ni lo declara aprobado sin esa evidencia real, siguiendo el mismo criterio que ya dejaron F4-01
(escaneo CVE pendiente de CI real) y F4-02 sección 5, punto 3 (`kubectl apply --dry-run` pendiente de un
clúster real).

---

## 6. Referencias

- [`plan-maestro-bitcode-ia.md`](plan-maestro-bitcode-ia.md) — filas F4-02/F4-04/F4-05/F4-06 del backlog (Fase 4).
- [`politica-contenedores.md`](politica-contenedores.md) — F4-01, la imagen que este paquete despliega
  (imagen "chiseled", por qué `lifecycle.preStop` de F4-05 usa la acción `sleep` nativa, no `exec`).
- [`guia-health-checks.md`](guia-health-checks.md) — F1-25, semántica de `/health/live` y `/health/ready`
  que consumen los probes de F4-04.
- [`guia-inbox-consumer.md`](guia-inbox-consumer.md) — F3-04, patrón de referencia del loop de
  `KafkaEventConsumer<TEvent>` que respeta `stoppingToken` (verificado sin cambios en F4-05, sección
  5-ter de este documento).
- [`guia-outbox-publisher.md`](guia-outbox-publisher.md) — F3-03, `OutboxPublisherBackgroundService`
  (verificado sin cambios en F4-05, sección 5-ter de este documento).
- [`adr/0007-gateway-yarp.md`](adr/0007-gateway-yarp.md) — por qué el `Service` es `ClusterIP` sin
  exposición externa todavía (F4-08 la agrega).
- [`adr/0014-secretos-proveedor-vault-propuesto.md`](adr/0014-secretos-proveedor-vault-propuesto.md) /
  [`guia-secret-provider.md`](guia-secret-provider.md) — por qué el `Secret` referenciado no se define en
  este repositorio.
- `src/Shared.Infrastructure.Observability/ObservabilityServiceCollectionExtensions.cs` — F3-10,
  confirma que `AddSharedObservability` solo exporta métricas vía OTLP (sin endpoint Prometheus), base
  del TODO de métrica de aplicación documentado en la sección 5-quater (F4-06).
- `k8s/sample-api/` — implementación de esta política.
