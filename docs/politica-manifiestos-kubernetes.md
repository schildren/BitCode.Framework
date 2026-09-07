# Manifiestos de despliegue Kubernetes — BitCode.Framework

**Tarea:** F4-02 (Fase 4 — Runtime de alta disponibilidad) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Fecha:** 2026-09-07
**Estado:** Aplicado. El criterio de aceptación de la fila F4-02 es "Configuración validada": no hay un
clúster Kubernetes real disponible en este entorno, así que la validación se hizo con herramientas
estáticas reales contra los manifiestos generados (evidencia en la sección 5) — igual que F4-01 dejó
"escaneo sin CVE crítica" confirmado localmente y pendiente de la primera corrida en CI real.

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
│   ├── deployment.yaml       # 1 réplica base, TODOs explícitos para F4-04/F4-05/F4-07
│   ├── service.yaml          # ClusterIP:80 -> containerPort 8080 (http)
│   ├── configmap.yaml        # Config no sensible (ASPNETCORE_ENVIRONMENT, logging, Secrets:Provider)
│   └── secret.example.yaml   # PLANTILLA documental, no se aplica ni se referencia desde kustomization.yaml
└── overlays/
    ├── dev/
    │   ├── kustomization.yaml   # namespace bitcode-sample-api-dev, tag "dev"
    │   ├── namespace.yaml
    │   ├── configmap-patch.yaml # ASPNETCORE_ENVIRONMENT=Development, logging Debug
    │   └── deployment-patch.yaml # replicas: 1, requests/limits reducidos
    ├── staging/
    │   ├── kustomization.yaml   # namespace bitcode-sample-api-staging, tag "0.1.0" (inmutable)
    │   ├── namespace.yaml
    │   ├── configmap-patch.yaml # ASPNETCORE_ENVIRONMENT=Staging
    │   └── deployment-patch.yaml # replicas: 2, requests/limits intermedios
    └── prod/
        ├── kustomization.yaml   # namespace bitcode-sample-api-prod, tag "0.1.0" (inmutable)
        ├── namespace.yaml
        ├── configmap-patch.yaml # ASPNETCORE_ENVIRONMENT=Production, Secrets:Provider=Vault
        └── deployment-patch.yaml # replicas: 3 (piso mínimo, tolera pérdida de 1 nodo), requests/limits altos
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

**Deliberadamente fuera de alcance (tareas futuras del backlog, marcadas con `TODO(F4-0N)` en
`deployment.yaml`), para no mezclar el alcance de tareas distintas (Plan Maestro sección 3.2):**

- **Probes** (`startupProbe`/`livenessProbe`/`readinessProbe`) — F4-04. `Sample.Api` todavía no expone
  endpoints de health/readiness; declarar un probe HTTP contra un endpoint inexistente rompería el pod
  al desplegar (bucle de reinicio) en vez de solo documentar la brecha, así que no se declaró ningún
  probe todavía.
- **Shutdown graceful** (`preStop`, `terminationGracePeriodSeconds`) — F4-05.
- **HPA** (`HorizontalPodAutoscaler`) — F4-06. Los `replicas` fijos actuales (1/2/3 por ambiente) son el
  piso que el HPA de esa tarea tomará como mínimo, no lo reemplazan.
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

## 6. Referencias

- [`plan-maestro-bitcode-ia.md`](plan-maestro-bitcode-ia.md) — fila F4-02 del backlog (Fase 4).
- [`politica-contenedores.md`](politica-contenedores.md) — F4-01, la imagen que este paquete despliega.
- [`adr/0007-gateway-yarp.md`](adr/0007-gateway-yarp.md) — por qué el `Service` es `ClusterIP` sin
  exposición externa todavía (F4-08 la agrega).
- [`adr/0014-secretos-proveedor-vault-propuesto.md`](adr/0014-secretos-proveedor-vault-propuesto.md) /
  [`guia-secret-provider.md`](guia-secret-provider.md) — por qué el `Secret` referenciado no se define en
  este repositorio.
- `k8s/sample-api/` — implementación de esta política.
