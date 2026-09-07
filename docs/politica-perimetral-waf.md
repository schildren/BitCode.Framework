# Política perimetral: WAF y límites (F4-09)

**Tarea:** F4-09 (Fase 4 — Runtime de alta disponibilidad) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Fecha:** 2026-09-07
**Estado:** Política declarada y validada sintácticamente. El criterio de aceptación de la fila F4-09 es
"Casos abusivos bloqueados" — se cumple **parcialmente con evidencia real** (ver sección 5): los casos
cubiertos por código de este repositorio (rate limiting de aplicación del Gateway, F4-08; tamaño de
request body, F4-09) están probados con pruebas de integración reales contra el Gateway real. Los casos
que dependen de un WAF/Ingress Controller perimetral real (firmas de ataque, rate limit por IP a nivel
de red, tamaño/timeout antes de llegar al proceso .NET) **no se pudieron ejercitar en tráfico real** en
este entorno — no hay clúster Kubernetes ni Ingress Controller/WAF real disponible (misma limitación que
`docs/politica-manifiestos-kubernetes.md` ya documentó para F4-02/F4-04/F4-05/F4-06/F4-07). Esta
distinción se mantiene explícita en todo el documento, no se declara el criterio cumplido al 100%.

---

## 1. Topología: dos capas de defensa, no una

```
Internet
   │
   ▼
┌─────────────────────────────────────┐
│ Capa perimetral (F4-09, esta tarea)  │  Ingress Controller (NGINX Ingress, de referencia)
│ - Tamaño máx. de payload             │  o WAF cloud-managed equivalente (ver sección 4)
│ - Timeouts (conexión/lectura/envío)  │
│ - Rate limit por IP                  │
│ - (Opcional/no cubierto) firmas CRS  │
└─────────────────────────────────────┘
   │  (tráfico ya filtrado)
   ▼
┌─────────────────────────────────────┐
│ BitCode.Gateway (F4-08, YARP)        │  Proceso .NET, dentro del clúster
│ - Auth boundary (JWT antes de proxy) │
│ - Rate limiting de aplicación        │
│ - Límite de tamaño de body (F4-09)   │
│ - Sanitización de headers internos   │
└─────────────────────────────────────┘
   │
   ▼
Backend(s) (sample-api, etc. — ClusterIP, nunca expuestos directamente)
```

Ninguna de las dos capas reemplaza a la otra — es defensa en profundidad deliberada:

- La capa perimetral protege recursos que el propio proceso .NET del Gateway no puede proteger por sí
  solo de forma barata (ancho de banda de red consumido por un payload gigante antes de que Kestrel
  siquiera empiece a leerlo, saturación de conexiones TCP tipo Slowloris, tráfico distribuido entre
  muchas IPs de origen antes de que llegue a un único proceso).
- La capa de aplicación (Gateway) protege lo que solo el proceso puede evaluar con contexto real
  (identidad del token, política de negocio, contenido del payload ya parseado) y sigue siendo necesaria
  incluso si el WAF perimetral real todavía no está desplegado (entornos de desarrollo, pruebas de
  integración, o mientras la decisión de vendor de la sección 4 no se resuelve).

---

## 2. Qué cubre esta tarea (F4-09) — con evidencia real

### 2.1. Ingress declarativo (`k8s/gateway/ingress.yaml`, `networking.k8s.io/v1`)

Expone el `Service` `gateway` (F4-08, `LoadBalancer`) a través de un `Ingress` con anotaciones de
**NGINX Ingress Controller** (`ingress-nginx`) — el controller de referencia elegido porque el Plan
Maestro no fija un proveedor cloud específico para esta pieza (sección 2 del Plan Maestro no lista un
WAF/CDN como decisión rectora), y `ingress-nginx` es el controller más ampliamente adoptado en clusters
Kubernetes cloud-agnósticos (coherente con el resto de `k8s/`, que tampoco asume un proveedor cloud
concreto — ver `docs/politica-manifiestos-kubernetes.md` sección 1, misma filosofía de no atarse a
tooling propietario sin necesidad).

| Anotación | Valor | Propósito |
|---|---|---|
| `nginx.ingress.kubernetes.io/proxy-body-size` | `10m` | Tamaño máximo de payload — rechaza antes de que el Gateway lo reciba. Mismo valor que el límite de aplicación (sección 2.2) — dos capas, mismo número, para que un cliente vea el mismo comportamiento efectivo en ambas. |
| `nginx.ingress.kubernetes.io/proxy-connect-timeout` | `5` (s) | Protección contra un backend/Gateway lento en aceptar la conexión. |
| `nginx.ingress.kubernetes.io/proxy-read-timeout` / `proxy-send-timeout` | `30` (s) | Protección contra conexiones lentas tipo Slowloris y contra un backend colgado reteniendo un worker de NGINX indefinidamente. |
| `nginx.ingress.kubernetes.io/limit-rps` + `limit-burst-multiplier` | `20` req/s por IP, ráfaga x3 | Rate limiting **por IP de origen**, a nivel de red — defensa adicional al rate limiting de aplicación del Gateway (F4-08, ventana fija global, no por IP). |

**Validación realizada en este entorno** (sin clúster ni Ingress Controller real disponible, mismo
tratamiento que el resto de `k8s/`):

1. `kubectl kustomize k8s/gateway` (kubectl 1.34.1, Kustomize v5.7.1 embebido) — el `Ingress` se fusiona
   sin error junto al resto de recursos del paquete (`ConfigMap`, `Service`, `Deployment`).
2. `kubeconform` v0.8.0 en modo `-strict` contra el YAML final, validado contra el esquema real de
   Kubernetes 1.30:
   ```
   kubeconform -strict -summary -kubernetes-version 1.30.0 rendered_gateway.yaml
   ```
   Resultado: `Valid: 4, Invalid: 0, Errors: 0, Skipped: 0` (`ConfigMap`, `Service`, `Deployment`,
   `Ingress`). **Nota sobre las anotaciones custom de NGINX:** contrario a lo anticipado, `kubeconform`
   **no rechazó** las anotaciones `nginx.ingress.kubernetes.io/*` en modo `-strict` — el esquema oficial
   de `Ingress` valida `metadata.annotations` como un mapa `string → string` genérico (cualquier clave es
   sintácticamente válida a nivel de Kubernetes API; NGINX Ingress Controller es quien les da semántica
   en tiempo de ejecución, no el API server). `-strict` sí sigue rechazando campos estructurales
   desconocidos en `spec`/`status`, que es lo que protege contra typos reales de esquema. Documentado
   explícitamente porque el enunciado de esta tarea anticipaba una posible falla que finalmente no
   ocurrió — no se ocultó el resultado real.
3. `kubectl apply --dry-run=client` — **no se pudo completar**, misma limitación ya documentada en
   `docs/politica-manifiestos-kubernetes.md` sección 5, punto 3 (esta versión de `kubectl` requiere
   descubrir grupos de API contra un servidor real incluso en `--dry-run=client`).

**Lo que NO se comprobó** (brecha explícita, no oculta): no hay Ingress Controller `ingress-nginx` real
instalado en ningún clúster de este entorno, así que **ninguna de las anotaciones de la tabla se
ejercitó contra tráfico real** — no se envió un payload de 11 MiB contra un Ingress real esperando
`413`, ni se generó carga sostenida a más de 20 req/s por IP esperando `503`. Confirmar el
comportamiento real de estas anotaciones contra un clúster con `ingress-nginx` desplegado queda para
cuando exista uno disponible — mismo criterio que F4-06 dejó pendiente "escala bajo carga" y F4-07
"pérdida de nodo sin caída" en ejecución real.

### 2.2. Límite de tamaño de request body en el Gateway (código, `src/BitCode.Gateway/RequestLimits/`)

Defensa en profundidad de **aplicación**, complementaria al `proxy-body-size` del Ingress (sección 2.1)
— protege al Gateway aunque no haya un Ingress/WAF real delante todavía (desarrollo, tests, o mientras
la decisión de vendor de la sección 4 no se resuelve):

- `GatewayRequestLimitsOptions` (sección de configuración `"RequestLimits"`, clave
  `MaxRequestBodySizeBytes`, default 10 MiB — mismo valor que `proxy-body-size` del Ingress).
- `MaxRequestBodySizeMiddleware`: rechaza con **`413 Payload Too Large`** cualquier request cuyo header
  `Content-Length` declarado supere el límite — **antes** de `UseAuthentication`/`UseRateLimiter`/
  `MapReverseProxy` (mismo criterio de "rechazar barato antes de trabajo caro" que ya aplicaba el rate
  limiting de F4-08). Para el caso sin `Content-Length` (chunked transfer encoding), ajusta
  `IHttpMaxRequestBodySizeFeature` para que Kestrel corte la conexión si el cuerpo real supera el límite
  durante la lectura del stream.

**Validación realizada — evidencia real, contra el Gateway real (no simulada):**
`tests/BitCode.Gateway.Tests/Integration/GatewayIntegrationTests.cs` (`WebApplicationFactory<Program>`
sobre el `Program.cs` real del Gateway, backend HTTP real de destino — mismo patrón que el resto de la
clase, F4-08):

- `Proxy_ConBodyQueSuperaElLimiteConfigurado_Rechaza413AntesDeAutenticarNiProxyar`: con
  `RequestLimits:MaxRequestBodySizeBytes = 1024` y un body de 2048 bytes, **sin token** — confirma
  `413` (y confirma que el rechazo ocurre antes de auth: si el orden del pipeline se rompiera, este
  request devolvería `401` en vez de `413`).
- `Proxy_ConBodyDentroDelLimiteConfigurado_NoLoRechazaPorTamano`: con un body de 100 bytes (dentro del
  límite de 1024), confirma que la respuesta **no** es `413` (el middleware deja pasar el request).

Este es el único caso abusivo de esta tarea con evidencia real contra código ejecutable — el resto
(sección 2.1) queda como política declarada, sin poder ejercitarse en tráfico real en este entorno.

---

## 3. Qué NO cubre esta tarea — brechas explícitas

- **Reglas de firmas de ataques comunes (SQLi/XSS, OWASP ModSecurity Core Rule Set o equivalente).**
  Requieren el módulo ModSecurity (u otro motor de firmas) compilado/habilitado dentro de la imagen del
  Ingress Controller — no es una anotación simple de `ingress-nginx` estándar (la imagen oficial de
  `ingress-nginx` no trae ModSecurity habilitado por defecto; requiere una imagen alternativa o un
  segundo controller dedicado, p. ej. `ingress-nginx` compilado con `--with-http_modsecurity_module`, o
  el operador OWASP ModSecurity CRS). No se declara como si estuviera activo — sería una anotación
  vacía sin efecto real, peor que documentarlo como pendiente.
- **TLS/HTTPS en el `Ingress`.** `k8s/gateway/ingress.yaml` deja `spec.tls` fuera deliberadamente:
  requiere un certificado real (`cert-manager` + `Issuer`, o un certificado provisto externamente) y un
  dominio real, ninguno disponible en este entorno — mismo criterio que
  `k8s/sample-api/base/secret.example.yaml` nunca commitea secretos reales.
- **Bloqueo geográfico / listas de reputación de IP (threat intelligence feeds).** No evaluado; requiere
  un WAF con esa capacidad (cloud-managed o ModSecurity + feeds externos), fuera del alcance de un
  `Ingress Controller` genérico.
- **Protección DDoS volumétrica de capa 3/4** (fuera de lo que un Ingress Controller de capa 7 puede
  mitigar) — típicamente responsabilidad de la capa de red/CDN del proveedor cloud, no de Kubernetes.

---

## 4. Decisión de vendor de WAF real — NO tomada, solo opciones documentadas

Elegir un WAF concreto de un proveedor cloud específico (Azure Front Door WAF / AWS WAF / Cloudflare WAF
/ Google Cloud Armor, etc.) es una **decisión de infraestructura/vendor no trivial** — implica costo
recurrente, dependencia de un proveedor cloud específico (el resto del stack de BitCode se mantiene
deliberadamente cloud-agnóstico, ver ADR 0001/0008), y potencialmente una licencia o SLA contractual.
Cae dentro de las categorías del Plan Maestro (sección 13) que requieren **aprobación humana explícita**
antes de tomarse como decisión operativa — no se toma en esta tarea, se documentan las opciones:

| Opción | Cuándo tendría sentido | Trade-off principal |
|---|---|---|
| `ingress-nginx` + anotaciones (esta tarea) sin firmas de ataque | Cluster propio, sin presupuesto/decisión de WAF cloud-managed todavía | Sin protección de firmas SQLi/XSS; solo tamaño/timeout/rate-limit por IP |
| `ingress-nginx` compilado con ModSecurity + OWASP CRS | Cluster propio, se necesita protección de firmas sin depender de un cloud vendor | Mantenimiento propio del ruleset, más CPU por request, requiere imagen custom del controller |
| WAF cloud-managed (Azure Front Door / AWS WAF / Cloudflare / Cloud Armor) | Se decide operar en un cloud provider específico y aceptar esa dependencia | Costo recurrente, atado a un proveedor, requiere decisión y aprobación humana explícita (Plan Maestro sección 13) |

**No se activa ninguna de estas opciones como si ya estuviera desplegada.** Queda como recomendación
para cuando el proyecto consumidor real defina su plataforma de despliegue (Fase 9, extracción de
microservicios / habilitación de tráfico productivo, ambas también sujetas a aprobación humana según la
sección 13 del Plan Maestro).

---

## 5. Cierre respecto al criterio de aceptación ("Casos abusivos bloqueados")

| Caso abusivo | Capa | Evidencia |
|---|---|---|
| Payload sobredimensionado (con `Content-Length`) | Gateway (aplicación) | **Real** — test de integración, `413` confirmado |
| Payload sobredimensionado (sin `Content-Length` declarado, streaming) | Gateway (aplicación) | Declarado vía `IHttpMaxRequestBodySizeFeature`, no probado con un stream chunked real en esta tarea |
| Payload sobredimensionado, antes de llegar al Gateway | Ingress/WAF perimetral | Política declarada (`proxy-body-size`), **no ejercitada contra tráfico real** |
| Ráfaga de requests que supera el límite de aplicación | Gateway (aplicación) | **Real** — ya probado en F4-08 (`Proxy_ConTokenValido_SuperarLimiteDeRateLimiting_Rechaza429`), sin cambios en esta tarea |
| Ráfaga de requests por IP, antes de llegar al Gateway | Ingress/WAF perimetral | Política declarada (`limit-rps`), **no ejercitada contra tráfico real** |
| Conexión lenta / colgada (Slowloris) | Ingress/WAF perimetral | Política declarada (timeouts), **no ejercitada contra tráfico real** |
| Payload con firma de ataque conocida (SQLi/XSS) | WAF de firmas (CRS o equivalente) | **Fuera de alcance** — ver sección 3, no declarado como cubierto |

**Conclusión:** el criterio de aceptación se cumple parcialmente con evidencia real (casos de código,
Gateway) y queda declarado — no demostrado en ejecución — para los casos que dependen de un
Ingress Controller/WAF perimetral real. Esta distinción se deja explícita en la tabla de arriba, mismo
criterio que el Plan Maestro (sección 3.6) exige para no marcar "Completada" una validación que no se
pudo ejecutar.

---

## 6. Referencias

- [`plan-maestro-bitcode-ia.md`](plan-maestro-bitcode-ia.md) — fila F4-09 del backlog (Fase 4).
- [`politica-manifiestos-kubernetes.md`](politica-manifiestos-kubernetes.md) — mismo tratamiento de
  "validado sintácticamente, sin clúster real disponible" para el resto de `k8s/`.
- `k8s/gateway/ingress.yaml` — implementación de la política perimetral declarativa.
- `src/BitCode.Gateway/RequestLimits/` — implementación del límite de tamaño de body de aplicación.
- `tests/BitCode.Gateway.Tests/Integration/GatewayIntegrationTests.cs` — evidencia real (413, y el
  rate limiting de aplicación ya probado en F4-08).
- [`adr/0007-gateway-yarp.md`](adr/0007-gateway-yarp.md) — decisión de YARP como Gateway, y por qué su
  habilitación productiva (y la de cualquier WAF real delante) requiere aprobación humana separada
  (Plan Maestro sección 13).
