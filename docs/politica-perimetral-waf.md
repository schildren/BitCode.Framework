# Política perimetral: WAF y límites (F4-09)

**Tarea:** F4-09 (Fase 4 — Runtime de alta disponibilidad) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Fecha:** 2026-09-07 (revisión: cierre de pendiente, misma fecha — decisión de WAF de firmas tomada).
**Estado:** Política declarada y validada sintácticamente, con la decisión de WAF de firmas ya TOMADA
(sección 4 — ModSecurity + OWASP CRS vía `ingress-nginx`, ya no queda como opciones abiertas). El
criterio de aceptación de la fila F4-09 es "Casos abusivos bloqueados" — se cumple **parcialmente con
evidencia real** (ver sección 5): los casos cubiertos por código de este repositorio (rate limiting de
aplicación del Gateway, F4-08, incluido el rate limiting DISTRIBUIDO entre réplicas vía Redis, cierre de
pendiente de F4-08; tamaño de request body, F4-09) están probados con pruebas de integración reales
contra el Gateway real. Los casos que dependen de un WAF/Ingress Controller perimetral real (firmas de
ataque vía ModSecurity+CRS, rate limit por IP a nivel de red, tamaño/timeout antes de llegar al proceso
.NET) **no se pudieron ejercitar en tráfico real** en este entorno — no hay clúster Kubernetes ni Ingress
Controller/WAF real disponible (misma limitación que `docs/politica-manifiestos-kubernetes.md` ya
documentó para F4-02/F4-04/F4-05/F4-06/F4-07). Esta distinción se mantiene explícita en todo el
documento, no se declara el criterio cumplido al 100%.

---

## 1. Topología: dos capas de defensa, no una

```
Internet
   │
   ▼
┌─────────────────────────────────────┐
│ Capa perimetral (F4-09, esta tarea)  │  Ingress Controller (NGINX Ingress + ModSecurity/OWASP CRS,
│ - Tamaño máx. de payload             │  decisión tomada -- sección 4; requiere imagen con el módulo
│ - Timeouts (conexión/lectura/envío)  │  ModSecurity compilado, ver sección 4.1)
│ - Rate limit por IP                  │
│ - Firmas SQLi/XSS (ModSecurity+CRS,  │
│   modo DetectionOnly al arrancar)    │
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
  integración, o mientras no exista un clúster real con el Ingress Controller/módulo ModSecurity
  desplegado, ver sección 4.1).

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
| `nginx.ingress.kubernetes.io/limit-rps` + `limit-burst-multiplier` | `20` req/s por IP, ráfaga x3 | Rate limiting **por IP de origen**, a nivel de red — defensa adicional al rate limiting de aplicación del Gateway (F4-08, ventana fija global, no por IP; distribuida entre réplicas vía Redis si está configurado, ver sección 2.3). |
| `nginx.ingress.kubernetes.io/enable-modsecurity` + `enable-owasp-core-rules` | `"true"` / `"true"` | WAF de firmas (F4-09, decisión tomada — sección 4): habilita ModSecurity + el ruleset OWASP CRS dentro de `ingress-nginx`. Requiere una imagen del controller con el módulo ModSecurity compilado (ver sección 4.1) — sin ese requisito, NGINX ignora estas anotaciones en silencio. |
| `nginx.ingress.kubernetes.io/modsecurity-snippet` | `SecRuleEngine DetectionOnly` | Arranque en modo detección/auditoría, NO bloqueo (sección 4.2) — evita cortar tráfico legítimo por falsos positivos del CRS genérico antes de haber observado tráfico real. |

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
no exista un clúster real con el Ingress Controller desplegado, ver sección 4.1):

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

### 2.3. Rate limiting distribuido entre réplicas (código, `src/BitCode.Gateway/RateLimiting/`)

Cierre de pendiente de F4-08, no de F4-09, documentado acá porque comparte topología con esta política:
antes de este cierre, el rate limiting de aplicación del Gateway (fila de la tabla de la sección 2.1)
usaba el rate limiter nativo de ASP.NET Core, **en memoria por proceso** — con `replicas: 2` (o más) del
Gateway, el límite efectivo era `N * PermitLimit`, no `PermitLimit`. Cerrado con
`RedisFixedWindowRateLimiter` (`src/BitCode.Gateway/RateLimiting/RedisFixedWindowRateLimiter.cs`): mismo
algoritmo de ventana fija, pero el contador vive en Redis (compartido entre TODAS las réplicas),
incrementado y expirado dentro de un único `EVAL` de Lua atómico (sin condición de carrera de
"leer-luego-escribir" entre réplicas concurrentes). Con `Caching:RedisConnectionString` configurado
(misma clave que ya usa el resto del framework, F4-03), el límite configurado (`RateLimiting:PermitLimit`)
es el límite AGREGADO real. Sin Redis configurado, cae al rate limiter en memoria previo (fallback
documentado, no rompe el caso sin Redis).

**Validación realizada — evidencia real, contra DOS instancias reales del Gateway y Redis real (no
simulada):** `tests/BitCode.Gateway.Tests/Integration/GatewayDistributedRateLimitingIntegrationTests.cs`
(dos `WebApplicationFactory<Program>` independientes — dos hosts/DI distintos, simulando dos réplicas
reales de un mismo Deployment — contra un contenedor Redis real vía Testcontainers):

- `DosInstanciasDelGateway_ConRedisConfigurado_RespetanElLimiteDeFormaAgregadaEntreAmbas`: con
  `PermitLimit = 4`, se reparten 4 requests entre las dos instancias (2 y 2) — el quinto request, contra
  CUALQUIERA de las dos instancias, se rechaza con `429` aunque esa instancia individualmente solo
  llevaba 2 requests propios. Esta es la evidencia central: el límite se respeta de forma AGREGADA, no
  por instancia.
- `DosInstanciasDelGateway_SinRedisConfigurado_CadaUnaAplicaSuPropioLimiteEnMemoria`: control/contraste —
  sin Redis configurado, cada instancia agota su propia cuota completa de forma independiente (el doble
  de requests exitosos en total que `PermitLimit`), confirmando que el fallback documentado sigue
  funcionando y dejando en evidencia, por contraste directo, la limitación que este cierre resuelve.

A diferencia de las anotaciones del Ingress (sección 2.1), este caso SÍ tiene evidencia real de dos
réplicas concurrentes contra un backend compartido real — no depende de un clúster Kubernetes ni de un
Ingress Controller real, corre completamente en proceso .NET + Redis real.

---

## 3. Qué NO cubre esta tarea — brechas explícitas

- **Reglas de firmas de ataques comunes (SQLi/XSS, OWASP ModSecurity Core Rule Set).** Ya NO es una
  brecha sin decisión — ver sección 4 (decisión tomada: ModSecurity + OWASP CRS vía `ingress-nginx`,
  `k8s/gateway/ingress.yaml`). Sigue siendo una brecha de **verificación** (no de decisión): requiere una
  imagen del Ingress Controller con el módulo ModSecurity compilado (sección 4.1), y no hay un clúster
  real en este entorno para confirmar que las reglas efectivamente bloquean/auditan tráfico con firmas
  de ataque reales — la anotación queda declarada y validada solo sintácticamente (sección 4.3), no
  ejercitada contra tráfico real.
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

## 4. Decisión de WAF de firmas — TOMADA (self-hosted, ModSecurity + OWASP CRS)

**Decisión tomada para este entorno self-hosted:** `ingress-nginx` compilado/configurado con el módulo
**ModSecurity + OWASP Core Rule Set (CRS)**, aplicado en `k8s/gateway/ingress.yaml`
(`nginx.ingress.kubernetes.io/enable-modsecurity`, `enable-owasp-core-rules`, `modsecurity-snippet`). No
queda como "opciones sin decidir" — este documento anteriormente dejaba la elección abierta; se cierra
acá porque **no involucra elegir un vendor/proveedor cloud** (la categoría de decisión que sí requiere
aprobación humana explícita según la sección 13 del Plan Maestro): ModSecurity+CRS es software open
source (licencia Apache 2.0/BSD según el componente) que corre **dentro del mismo Ingress Controller ya
elegido** (`ingress-nginx`, sección 2.1) — no suma una pieza de infraestructura nueva, ni un contrato, ni
un costo recurrente, ni un lock-in de proveedor.

**Por qué esta opción y no un WAF cloud-managed:**

| Opción | Decisión | Razón |
|---|---|---|
| `ingress-nginx` + ModSecurity + OWASP CRS | **Tomada, aplicada en esta tarea** | Open source, sin costo de licencia, corre en el mismo Ingress Controller ya elegido (sin infraestructura nueva), estándar de facto para WAF perimetral basado en firmas cuando no hay un proveedor cloud-managed ya decidido. Trade-off aceptado: mantenimiento propio del ruleset (ajuste de falsos positivos, ver sección 4.2) y algo más de CPU por request. |
| WAF cloud-managed (Azure Front Door WAF / AWS WAF / Cloudflare / Google Cloud Armor) | **Puerta abierta, NO tomada** | Requiere decidir operar en un cloud provider específico — el resto del stack de BitCode se mantiene deliberadamente cloud-agnóstico (ADR 0001/0008). Costo recurrente, dependencia de vendor, y cae dentro de las categorías del Plan Maestro (sección 13) que requieren **aprobación humana explícita** antes de tomarse como decisión operativa. Queda como alternativa futura SI el proyecto consumidor decide migrar a un cloud provider concreto (Fase 9, extracción de microservicios / habilitación de tráfico productivo, ambas también sujetas a aprobación humana). |

### 4.1. Requisito de infraestructura explícito — no asumible sin verificación

La imagen **oficial** `registry.k8s.io/ingress-nginx/controller` **no trae ModSecurity compilado por
defecto**. Un despliegue real de esta política necesita una de estas dos opciones (no evaluadas ni
elegidas entre sí en esta tarea, es responsabilidad del ambiente real):

1. `registry.k8s.io/ingress-nginx/controller-chroot` (incluye ModSecurity desde `ingress-nginx` >= 1.9),
   o
2. una imagen custom del controller compilada con `--with-http_modsecurity_module`.

Aplicar `k8s/gateway/ingress.yaml` contra un controller **sin** ese módulo no falla — las anotaciones
`enable-modsecurity`/`enable-owasp-core-rules`/`modsecurity-snippet` quedan simplemente sin efecto real
(NGINX las ignora en silencio). Este requisito queda documentado explícitamente en el propio manifiesto
(comentario junto a las anotaciones) para que no se asuma protección activa sin haberlo confirmado contra
un Ingress Controller real.

### 4.2. Modo de arranque: `DetectionOnly`, no bloqueo agresivo — plan de transición

El manifiesto arranca con `SecRuleEngine DetectionOnly` (audita/loguea matches del CRS sin bloquear el
request) — **deliberadamente, no un descuido**: el OWASP CRS genérico tiene una tasa de falsos positivos
no despreciable contra tráfico legítimo real (payloads JSON/form con caracteres o patrones que matchean
firmas SQLi/XSS de forma espuria, p. ej. un campo de texto libre con comillas o guiones). Activar bloqueo
real (`SecRuleEngine On`) en el primer despliegue, sin haber observado tráfico real primero, arriesga
cortar clientes legítimos sin que nadie lo haya decidido conscientemente.

**Plan de transición explícito** (a ejecutar por el proyecto consumidor real, con un clúster real
disponible — no ejecutable en este entorno):

1. Desplegar con `SecRuleEngine DetectionOnly` (estado de este manifiesto).
2. Observar los logs de auditoría de ModSecurity (`SecAuditLog`, vía el propio `ingress-nginx`) durante
   un período representativo de tráfico real (recomendado: al menos una semana completa, cubriendo el
   patrón de uso real de los endpoints expuestos) para identificar reglas del CRS que generan falsos
   positivos contra tráfico legítimo del proyecto.
3. Afinar el ruleset: deshabilitar/ajustar puntualmente las reglas identificadas como falsos positivos
   vía `SecRuleRemoveById`/`SecRuleUpdateTargetById` en un `modsecurity-snippet` ampliado (o un
   `ConfigMap` de reglas dedicado si el volumen de ajustes lo justifica) — nunca deshabilitar el CRS
   completo para "solucionar" un falso positivo puntual.
4. Recién con el ruleset afinado y validado contra tráfico real, pasar a `SecRuleEngine On` (bloqueo
   real) — decisión operativa del proyecto consumidor, no de este repositorio (no hay tráfico productivo
   real en este entorno para tomarla con evidencia).

### 4.3. Validación realizada en este entorno (sin clúster real disponible)

Mismo tratamiento que el resto de `k8s/gateway/` (sección 2.1): `kubectl kustomize k8s/gateway` fusiona
el `Ingress` con las anotaciones de ModSecurity sin error, y `kubeconform -strict -kubernetes-version
1.30.0` valida los 4 recursos (`ConfigMap`, `Service`, `Deployment`, `Ingress`) sin errores —
`Valid: 4, Invalid: 0, Errors: 0, Skipped: 0`, mismo resultado que antes de agregar las anotaciones
(`metadata.annotations` es un mapa `string → string` genérico, ver nota de la sección 2.1). **Lo que NO
se comprobó:** no hay Ingress Controller real (con o sin ModSecurity compilado) desplegado en este
entorno, así que no se pudo enviar un payload con una firma de ataque real (p. ej. `' OR '1'='1`) contra
un Ingress real esperando ver la entrada correspondiente en el log de auditoría de ModSecurity. Queda
pendiente de verificación contra un clúster real con el Ingress Controller correcto desplegado — mismo
criterio que el resto de `k8s/` (`docs/politica-manifiestos-kubernetes.md` sección 5).

---

## 5. Cierre respecto al criterio de aceptación ("Casos abusivos bloqueados")

| Caso abusivo | Capa | Evidencia |
|---|---|---|
| Payload sobredimensionado (con `Content-Length`) | Gateway (aplicación) | **Real** — test de integración, `413` confirmado |
| Payload sobredimensionado (sin `Content-Length` declarado, streaming) | Gateway (aplicación) | Declarado vía `IHttpMaxRequestBodySizeFeature`, no probado con un stream chunked real en esta tarea |
| Payload sobredimensionado, antes de llegar al Gateway | Ingress/WAF perimetral | Política declarada (`proxy-body-size`), **no ejercitada contra tráfico real** |
| Ráfaga de requests que supera el límite de aplicación (una sola réplica) | Gateway (aplicación) | **Real** — F4-08 (`Proxy_ConTokenValido_SuperarLimiteDeRateLimiting_Rechaza429`) |
| Ráfaga de requests que supera el límite AGREGADO entre réplicas (rate limiting distribuido) | Gateway (aplicación) | **Real** — cierre de pendiente F4-08, sección 2.3 (`DosInstanciasDelGateway_ConRedisConfigurado_RespetanElLimiteDeFormaAgregadaEntreAmbas`, dos instancias reales + Redis real) |
| Ráfaga de requests por IP, antes de llegar al Gateway | Ingress/WAF perimetral | Política declarada (`limit-rps`), **no ejercitada contra tráfico real** |
| Conexión lenta / colgada (Slowloris) | Ingress/WAF perimetral | Política declarada (timeouts), **no ejercitada contra tráfico real** |
| Payload con firma de ataque conocida (SQLi/XSS) | WAF de firmas (ModSecurity + OWASP CRS) | **Decisión tomada y aplicada** (sección 4) — anotaciones declaradas en `k8s/gateway/ingress.yaml`, validadas sintácticamente (sección 4.3); **no ejercitada contra tráfico real** (requiere clúster real con imagen ModSecurity, sección 4.1), y arranca en modo auditoría (`DetectionOnly`, sección 4.2), no bloqueo |

**Conclusión:** el criterio de aceptación se cumple parcialmente con evidencia real (casos de código,
Gateway, incluido el rate limiting distribuido entre réplicas) y queda declarado — no demostrado en
ejecución — para los casos que dependen de un Ingress Controller/WAF perimetral real. La decisión de WAF
de firmas que antes quedaba abierta (sección 4) ya está tomada y aplicada; lo que sigue sin evidencia
real es exclusivamente la ejecución contra un clúster real, no la decisión en sí. Esta distinción se deja
explícita en la tabla de arriba, mismo criterio que el Plan Maestro (sección 3.6) exige para no marcar
"Completada" una validación que no se pudo ejecutar.

---

## 6. Referencias

- [`plan-maestro-bitcode-ia.md`](plan-maestro-bitcode-ia.md) — fila F4-09 del backlog (Fase 4).
- [`politica-manifiestos-kubernetes.md`](politica-manifiestos-kubernetes.md) — mismo tratamiento de
  "validado sintácticamente, sin clúster real disponible" para el resto de `k8s/`.
- `k8s/gateway/ingress.yaml` — implementación de la política perimetral declarativa, incluida la
  decisión de WAF de firmas (ModSecurity + OWASP CRS, sección 4).
- `src/BitCode.Gateway/RequestLimits/` — implementación del límite de tamaño de body de aplicación.
- `src/BitCode.Gateway/RateLimiting/RedisFixedWindowRateLimiter.cs` — rate limiting distribuido entre
  réplicas (cierre de pendiente F4-08, sección 2.3).
- `tests/BitCode.Gateway.Tests/Integration/GatewayIntegrationTests.cs` — evidencia real (413, y el
  rate limiting de aplicación de una sola réplica ya probado en F4-08).
- `tests/BitCode.Gateway.Tests/Integration/GatewayDistributedRateLimitingIntegrationTests.cs` —
  evidencia real de dos instancias del Gateway compartiendo el límite agregado vía Redis real.
- [`adr/0007-gateway-yarp.md`](adr/0007-gateway-yarp.md) — decisión de YARP como Gateway, y por qué su
  habilitación productiva (y la de cualquier WAF real delante) requiere aprobación humana separada
  (Plan Maestro sección 13).
