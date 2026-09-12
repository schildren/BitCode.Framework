# Despliegue gradual — BitCode.Framework

**Tarea:** F4-13 (Fase 4 — Runtime de alta disponibilidad) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Fecha:** 2026-09-07
**Estado:** Aplicado parcialmente por diseño. El criterio de aceptación de la fila F4-13 es "Zero downtime
comprobado": esta tarea implementa y valida sintácticamente la estrategia `RollingUpdate` (`maxSurge: 1`,
`maxUnavailable: 0`) en los tres `Deployment` existentes (`k8s/sample-api/`, `k8s/gateway/`,
`k8s/otel-collector/`), y documenta canary/blue-green como recomendación para trabajo futuro — no los
implementa, porque requieren infraestructura/CRDs adicionales fuera del alcance de un cambio acotado (ver
sección 3). "Zero downtime **comprobado**" en el sentido literal de una prueba en ejecución contra un
clúster real con tráfico en curso queda para **F4-14 (Capacity tests)**, que incluye explícitamente
"Rolling deployment con tráfico" entre sus pruebas obligatorias — ver la sección 4 para el detalle de qué
evidencia sí existe hoy y qué no.

---

## 1. Estrategia por defecto: `RollingUpdate` nativo de Kubernetes

Los tres `Deployment` del repositorio (`k8s/sample-api/base/deployment.yaml`,
`k8s/gateway/deployment.yaml`, `k8s/otel-collector/deployment.yaml`) declaran ahora explícitamente:

```yaml
spec:
  strategy:
    type: RollingUpdate
    rollingUpdate:
      maxSurge: 1
      maxUnavailable: 0
```

**Por qué `RollingUpdate`, no canary/blue-green, como estrategia por defecto:**

- Es la estrategia **nativa** de `apps/v1 Deployment` — no requiere ningún componente adicional en el
  clúster (a diferencia de canary/blue-green formal, ver sección 3). Coherente con la misma decisión que
  ya tomó `docs/politica-manifiestos-kubernetes.md` sección 1 al elegir Kustomize sobre Helm: no sumar una
  dependencia de infraestructura nueva cuando el mecanismo nativo alcanza para el riesgo real del cambio.
- Es coherente con lo que **ya existe** en los manifiestos de F4-01 a F4-12: `readinessProbe` (F4-04) le da
  a Kubernetes una señal real y automática de "este pod nuevo ya puede recibir tráfico", que es exactamente
  lo que un `RollingUpdate` necesita para decidir cuándo reemplazar la siguiente réplica; `lifecycle.preStop`
  + `terminationGracePeriodSeconds` (F4-05) aseguran que el pod VIEJO termine de drenar antes de desaparecer;
  `PodDisruptionBudget` (F4-07) acota cuántas réplicas pueden faltar durante la disrupción voluntaria que el
  propio rollout genera. Ningún componente nuevo — el rollout se apoya en piezas ya construidas y validadas
  en tareas previas.
- Para el perfil de riesgo real de los tres workloads de este repositorio hoy (`sample-api`: API CRUD de
  referencia sin lógica de negocio compleja; `gateway`: proxy YARP sin estado propio; `otel-collector`:
  pipeline de telemetría sin estado persistente) — mismo criterio del punto 2 de la sección 3 — un
  `RollingUpdate` bien configurado (sin ventana de indisponibilidad, con la posibilidad de pausar/revertir
  vía `kubectl rollout pause`/`kubectl rollout undo`) cubre el riesgo real del cambio típico esperado en
  este repositorio. La sección 3 detalla cuándo ese perfil de riesgo cambiaría lo suficiente como para
  justificar canary/blue-green.

**Por qué estos valores concretos (`maxSurge: 1`, `maxUnavailable: 0`), no el default implícito de
Kubernetes (`maxSurge: 25%`, `maxUnavailable: 25%` si `strategy` no se declara):**

- `maxUnavailable: 0` es la pieza que hace la diferencia real para "zero downtime": el rollout crea el pod
  **nuevo** primero y espera a que su `readinessProbe` pase antes de sacar de servicio al pod **viejo** — en
  ningún momento del rollout hay menos réplicas SIRVIENDO tráfico que las declaradas en `spec.replicas`
  (o el piso `minReplicas` del HPA en `sample-api`, F4-06). El default `maxUnavailable: 25%` en cambio
  tolera sacar réplicas de servicio ANTES de que la reemplazante esté lista — con pocas réplicas (p. ej.
  `minReplicas: 1` en el overlay `dev` de `sample-api`), un `25%` redondeado hacia arriba puede dejar
  temporalmente 0 réplicas disponibles durante un rollout, exactamente lo que este criterio busca evitar.
- `maxSurge: 1` (no `25%`) acota a un único pod extra temporal por vez durante el rollout, evitando picos
  de consumo de `resources.requests` del namespace durante la transición — mismo criterio conservador que
  ya usan los `resources.requests`/`limits` base de cada `Deployment` (F4-02).
- **No declarar `strategy` explícitamente habría dejado el comportamiento real dependiendo del default
  implícito de la versión de Kubernetes del clúster** (documentado pero no garantizado que no cambie entre
  versiones) — declararlo hace el contrato de disponibilidad durante el rollout explícito y auditable en el
  propio manifiesto, no un supuesto implícito.

---

## 2. Verificación realizada (sin clúster real disponible en este entorno)

Misma limitación y mismo tratamiento que F4-02/F4-04/F4-05/F4-06/F4-07
(`docs/politica-manifiestos-kubernetes.md` secciones 5 a 5-quinquies): no hay un clúster Kubernetes real
disponible en este entorno, así que la validación se hizo con herramientas estáticas reales:

1. **`kubectl kustomize`** (Kustomize v5.7.1 embebido en `kubectl` 1.34.1) sobre los 3 overlays de
   `k8s/sample-api/` y sobre `k8s/gateway/`/`k8s/otel-collector/` (sin overlays, mismo alcance que F4-08/
   F4-10) — los cinco paquetes resuelven sin error, con `spec.strategy.type: RollingUpdate`,
   `rollingUpdate.maxSurge: 1`, `rollingUpdate.maxUnavailable: 0` presentes en el `Deployment` final de
   cada uno.
2. **`kubeconform` v0.8.0 en modo `-strict` contra el YAML final de cada paquete, validado contra el
   esquema real de Kubernetes 1.30**:
   ```
   kubeconform -strict -summary -kubernetes-version 1.30.0 rendered_sample_dev_f413.yaml
   kubeconform -strict -summary -kubernetes-version 1.30.0 rendered_sample_staging_f413.yaml
   kubeconform -strict -summary -kubernetes-version 1.30.0 rendered_sample_prod_f413.yaml
   kubeconform -strict -summary -kubernetes-version 1.30.0 rendered_gateway_f413.yaml
   kubeconform -strict -summary -kubernetes-version 1.30.0 rendered_otel_f413.yaml
   ```
   Resultado en los cinco casos: `Valid` para todos los recursos, `Invalid: 0, Errors: 0, Skipped: 0` —
   confirma que `spec.strategy` (`RollingUpdate` con `rollingUpdate.maxSurge`/`maxUnavailable` numéricos,
   no porcentuales) es sintácticamente válido contra el esquema oficial de `apps/v1 Deployment` en 1.30, sin
   romper la validación que ya pasaban estos mismos manifiestos antes de esta tarea (mismo número de
   recursos válidos por paquete que en `docs/politica-manifiestos-kubernetes.md` sección 5-quinquies /
   `docs/guia-otel-collector.md`).
3. **`docs/politica-configuracion-y-feature-flags.md` (F4-12)** ya deja documentado el mecanismo de
   "cambios controlados" a nivel de configuración de aplicación (feature flags, sin redeploy) — este
   documento cubre el nivel de **infraestructura** del rollout (reemplazo de pods), complementario, no
   redundante.

---

## 3. Canary y blue/green: recomendación para trabajo futuro, no implementados en esta tarea

El backlog (fila F4-13, "Rolling, canary o blue/green según riesgo") permite elegir cualquiera de las tres
según el riesgo del cambio. Esta tarea **no instala** un `Ingress Controller` con traffic splitting nativo
(p. ej. NGINX Ingress con `canary-weight`, Traefik con `TraefikService` ponderado) ni Argo Rollouts/Flagger
(que requieren su propio `CustomResourceDefinition` — `Rollout`/`Canary` — reemplazando al `Deployment`
nativo como el recurso que controla el rollout) ni un service mesh nuevo:

- Serían una **decisión de infraestructura mayor** para este entorno — instalar un nuevo controlador con
  sus propios CRDs, RBAC y superficie de fallo adicional, no un cambio acotado a los manifiestos ya
  construidos en F4-02/F4-08/F4-10. El Plan Maestro (sección 3.2) prohíbe mezclar refactors/decisiones
  ajenas al alcance de la tarea — agregar un controlador de canary nuevo sin que ninguna tarea previa lo
  haya preparado ni haya un ADR que lo respalde encaja en esa prohibición.
- Ningún cluster real está disponible en este entorno para siquiera instalar y probar esos componentes
  (misma limitación de fondo que ya documentan F4-02/F4-04/F4-05/F4-06/F4-07 — sección 5 de
  `docs/politica-manifiestos-kubernetes.md`).

**Recomendación documentada (no implementada) por nivel de riesgo, para cuando un consumidor del
framework la necesite:**

| Nivel de riesgo del cambio | Estrategia recomendada | Herramienta necesaria (no incluida en este repositorio) |
|---|---|---|
| Bajo (la mayoría de los cambios de `sample-api`/`gateway`/`otel-collector` hoy: fix de bug sin cambio de contrato, cambio de configuración no sensible vía `ConfigMap`, bump de versión de dependencia sin cambio de comportamiento observable) | **Rolling update** (implementado en esta tarea, sección 1) | Ninguna adicional — nativo de `apps/v1 Deployment`. |
| Medio (cambio de contrato de API con compatibilidad hacia atrás — ver `docs/gate-compatibilidad-api.md`/`docs/politica-versionado.md` —, cambio de esquema de base de datos con migración expand/contract, nueva dependencia externa) | **Rolling update con monitoreo activo durante el rollout** + posibilidad de pausar/revertir manualmente (`kubectl rollout pause`/`kubectl rollout undo deployment/<nombre>`) apoyado en las métricas OTel de F3-10/F4-10 (tasa de error, latencia por versión vía el atributo `service.version`/`k8s.pod.name` que ya propaga la telemetría mínima de la Fase 4) | Ninguna adicional para esta variante manual — un panel de observabilidad (Grafana u otro backend real que consuma del Collector de F4-10, aún no elegido, ver `docs/guia-otel-collector.md` sección 4) hace el monitoreo más ágil pero no es estrictamente necesario. |
| Alto (breaking change de contrato público, cambio de comportamiento de negocio de alto impacto, migración de datos irreversible, cambio que requiere aprobación humana de la sección 13 del Plan Maestro) | **Canary** (fracción de tráfico real a la versión nueva, incremento gradual con gates automáticos de error rate/latencia) o **blue/green** (dos entornos completos en paralelo, corte de tráfico atómico con posibilidad de rollback instantáneo) | Canary: un `Ingress Controller` con traffic splitting nativo (NGINX Ingress `canary-weight`, Traefik ponderado) **o** Argo Rollouts/Flagger (CRD `Rollout`/`Canary`, reemplaza al `Deployment` como recurso que controla el rollout, requiere métricas de un backend real tipo Prometheus para los gates automáticos). Blue/green: dos `Deployment`/`Service` completos (o dos `Ingress` con corte de DNS/label selector), sin CRD adicional pero con doble costo de cómputo durante la transición. Ninguno de los dos está instalado en este repositorio — instalarlos y elegir entre ellos requiere el ciclo completo de descubrimiento/diseño de una tarea propia (posiblemente con un ADR, dado que agrega un componente de infraestructura nuevo al clúster). |

Esta tabla es una **recomendación**, no una implementación — ningún cambio de código ni de manifiesto de
este repositorio activa canary/blue-green hoy. Un consumidor real que necesite alguna de las dos filas de
riesgo medio/alto debe abrir una tarea dedicada (sugerido: `/bitcode-adr` si involucra instalar un
componente de infraestructura nuevo, dado el impacto en el clúster).

---

## 4. "Zero downtime comprobado": qué evidencia existe hoy vs. qué queda para F4-14

El criterio de aceptación literal de la fila F4-13 es **"Zero downtime comprobado"**. Siguiendo el mismo
criterio que ya aplicaron F4-02/F4-04/F4-05/F4-06/F4-07 (no declarar cumplido lo que no se pudo comprobar
en ejecución real, Plan Maestro sección 3.6), se separa explícitamente:

**Evidencia real que sí existe (construida y validada en tareas previas, reutilizada acá):**

- **`readinessProbe` real (F4-04):** ya se demostró en ejecución (no solo por inspección de YAML, ver
  `docs/politica-manifiestos-kubernetes.md` sección 5-bis) que un pod sale del `Service` cuando su
  dependencia crítica falla, sin reiniciarse — la misma señal que un `RollingUpdate` usa automáticamente
  para decidir cuándo el pod nuevo ya puede recibir tráfico antes de reemplazar al viejo.
- **`lifecycle.preStop` + `terminationGracePeriodSeconds` (F4-05):** ya documentado (sección 5-ter del
  mismo archivo) que el pod viejo drena tráfico en curso antes de recibir `SIGTERM`, evitando que un
  rollout corte requests en vuelo.
- **`PodDisruptionBudget` (F4-07):** garantiza un piso de disponibilidad durante disrupciones voluntarias
  — el rollout de un `Deployment` (`kubectl set image`/`kubectl apply` con un cambio de imagen) genera
  exactamente ese tipo de disrupción, así que el PDB también acota cuántas réplicas puede sacar de
  servicio un rollout a la vez, en conjunto con `maxUnavailable: 0` de esta tarea.
- **`strategy.rollingUpdate` (esta tarea, sección 1):** validado sintácticamente contra el esquema real de
  Kubernetes 1.30 (sección 2) — confirma que el manifiesto declara correctamente la intención de zero
  downtime, no que un rollout real en ejecución la cumplió.

**Lo que NO se comprobó en esta tarea — explícitamente, no oculto:**

No hay un clúster Kubernetes real disponible en este entorno para ejecutar un rollout real (cambiar la
imagen de un `Deployment` ya desplegado) mientras se genera tráfico continuo contra el `Service`/`Ingress`
y medir si algún request se pierde o responde con error durante la transición — la única forma real de
"comprobar" zero downtime en el sentido literal del criterio de aceptación. Esto es exactamente lo que la
fila **F4-14 (Capacity tests)** del backlog cubre de forma explícita: sus pruebas obligatorias incluyen
**"Rolling deployment con tráfico"** entre carga, estrés, soak y escalamiento. Este documento y los
manifiestos que modifica dejan la estrategia declarada, validada sintácticamente y apoyada en piezas ya
probadas en ejecución (probes, shutdown, PDB) — pero el criterio de aceptación literal de F4-13 queda
**relacionado, no cerrado**, a la prueba real que ocurrirá en F4-14, mismo tratamiento que F4-06 dejó
pendiente "escala bajo carga" y F4-07 dejó pendiente "pérdida de nodo sin caída" en ejecución real.

---

## 5. CI: gate de manifiestos (`deploy-dev-dry-run`)

`.github/workflows/ci.yml` agrega el job `deploy-dev-dry-run` (`needs: build`, mismo patrón que
`dependency-scan`/`container-scan`) que automatiza — como parte del pipeline, no solo como pasos manuales
documentados acá — la misma validación estática de la sección 2, sobre los **cinco** paquetes de
manifiestos del repositorio (`k8s/sample-api/overlays/{dev,staging,prod}`, `k8s/gateway`,
`k8s/otel-collector`):

1. Instala `kubeconform` v0.8.0 (mismo binario y versión documentados en
   `docs/politica-manifiestos-kubernetes.md` sección 5) desde el release oficial de GitHub
   (`yannh/kubeconform`, Apache-2.0).
2. Corre `kubectl kustomize` (`kubectl` ya viene preinstalado en `ubuntu-latest`) sobre cada uno de los
   cinco paquetes.
3. Corre `kubeconform -strict -kubernetes-version 1.30.0` contra cada YAML renderizado — el pipeline falla
   (`exit code` distinto de 0) si algún manifiesto deja de ser válido contra el esquema oficial de
   Kubernetes, evitando que un cambio futuro rompa en silencio lo que hoy está validado manualmente.

**Deliberadamente NO incluido en este job — documentado, no simulado como si funcionara:**

- **Ningún despliegue real** (`kubectl apply -k ...`) contra ningún clúster: no hay credenciales ni
  `kubeconfig` de un cluster real (dev, staging o prod) disponibles ni configuradas como secreto de este
  repositorio en este momento — inventar un paso que "simule" un `apply` exitoso sin un clúster real
  detrás daría una falsa sensación de cobertura, contrario al Plan Maestro sección 3.2 (prohibido declarar
  una tarea terminada con controles de calidad deshabilitados o falseados) y sección 13 (habilitar tráfico
  productivo/aprovisionar infraestructura de un cluster real requiere aprobación humana explícita, fuera
  del alcance de este cambio).
- El paso siguiente natural — un job real `deploy-dev` que sí ejecute `kubectl apply -k
  k8s/sample-api/overlays/dev` contra un clúster de desarrollo real — queda documentado como trabajo
  pendiente explícito: requiere (a) aprovisionar ese clúster (o usar uno gestionado ya existente, decisión
  operativa fuera del alcance de esta tarea), y (b) configurar sus credenciales como secreto de GitHub
  Actions (`kubeconfig` o un mecanismo de autenticación federada tipo OIDC/workload identity, a decidir en
  una tarea/ADR dedicado — no una elección liviana, dado que toca la sección 13 del Plan Maestro si termina
  siendo un proveedor de identidad/credenciales nuevo).

---

## 6. Referencias

- [`plan-maestro-bitcode-ia.md`](plan-maestro-bitcode-ia.md) — fila F4-13 del backlog (Fase 4), y F4-14
  ("Capacity tests", incluye "Rolling deployment con tráfico" entre las pruebas obligatorias que comprueban
  en ejecución real el criterio que esta tarea deja declarado).
- [`politica-manifiestos-kubernetes.md`](politica-manifiestos-kubernetes.md) — F4-02/F4-04/F4-05/F4-06/
  F4-07, las piezas (`readinessProbe`, `lifecycle.preStop`, `PodDisruptionBudget`) sobre las que se apoya
  el `RollingUpdate` de esta tarea.
- [`guia-otel-collector.md`](guia-otel-collector.md) — F4-10, el tercer `Deployment` al que esta tarea le
  agrega `strategy`.
- [`politica-configuracion-y-feature-flags.md`](politica-configuracion-y-feature-flags.md) — F4-12, el
  mecanismo de "cambios controlados" a nivel de configuración de aplicación, complementario al rollout de
  infraestructura de esta tarea.
- [`gate-compatibilidad-api.md`](gate-compatibilidad-api.md) / [`politica-versionado.md`](politica-versionado.md)
  — criterios usados en la tabla de la sección 3 para clasificar el riesgo de un cambio de contrato.
- `k8s/sample-api/base/deployment.yaml`, `k8s/gateway/deployment.yaml`,
  `k8s/otel-collector/deployment.yaml` — implementación de `spec.strategy` de esta tarea.
- `.github/workflows/ci.yml` — job `deploy-dev-dry-run` de esta tarea.
