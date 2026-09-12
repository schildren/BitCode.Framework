# Contrato operativo — mTLS servicio-a-servicio (F2-14)

**Tarea:** F2-14 (Fase 2, Épica F2-C — Secretos y criptografía) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md).
**Entregable literal del backlog:** "Contratos operativos".
**Criterio de aceptación:** "Aplicable al extraer servicios" — no "mTLS operando hoy".
**ADR:** [`docs/adr/0015-mtls-contrato-operativo-servicio-a-servicio.md`](adr/0015-mtls-contrato-operativo-servicio-a-servicio.md)
— el contrato de este documento queda `Accepted`; el mecanismo concreto de emisión de certificados
(SPIFFE/SPIRE propuesto como candidato de referencia) queda `Proposed`, pendiente de aprobación humana
(Plan Maestro sección 13) antes de operarse contra infraestructura real.

## Cuándo aplica este contrato

**No aplica hoy.** `BitCode.Framework` es, al momento de esta tarea, un monolito modular de un único
proceso desplegable (`docs/plan-maestro-bitcode-ia.md`, Fase 1 completada; Fase 9 "Capacidad de
extracción de microservicios" todavía no alcanzada). No existen dos servicios separados en red que
necesiten autenticarse mutuamente por TLS. Este documento es **preparación**: define qué deberá cumplir
cualquier servicio antes de poder llamar o ser llamado por otro servicio del framework, para el momento en
que la Fase 9 extraiga el primer módulo elegible (`docs/plan-maestro-bitcode-ia.md`, Fase 9, "Criterios de
elegibilidad" y backlog F9-01 a F9-11).

Aplica, como Definition of Ready obligatoria, a partir de:

- **F9-05 (Host independiente):** ningún servicio extraído puede exponer su runtime independiente sin
  cumplir este contrato en su configuración de servidor (validación de certificado de cliente entrante) y
  de cliente saliente (presentación de su propio certificado a los servicios que consume).
- **F9-06 (Routing):** el gateway (YARP, ADR 0007) que enruta tráfico hacia/desde un servicio extraído
  debe decidir explícitamente si termina TLS en el borde (gateway↔cliente externo) y re-origina mTLS
  hacia el servicio interno, o si pasa mTLS de extremo a extremo — ver sección 5.

## 1. Qué NO reemplaza este contrato

mTLS **complementa**, no reemplaza, la identidad de servicio OAuth2 Client Credentials ya implementada
(F2-04, `docs/guia-oidc-adapter.md` sección "F2-04", `ADR 0004`):

| Capa | Mecanismo | Qué responde |
|---|---|---|
| Transporte/red | mTLS (este contrato) | "¿Qué proceso/workload es el que está del otro lado del socket TCP?" — identidad de infraestructura, verificada antes de que exista cualquier request HTTP. |
| Aplicación | OAuth2 Client Credentials (`IServiceTokenProvider`, F2-04) | "¿Qué permisos tiene esta llamada concreta? ¿Qué scope/audiencia autoriza?" — identidad de negocio, verificada por request vía el access token. |

Ambas capas son independientes y **defensa en profundidad** (Plan Maestro sección 1, Zero Trust —
"verificar siempre, en cada capa"), no alternativas: un servicio extraído con mTLS pero sin validar el
access token (o viceversa) no cumple Zero Trust. Ninguna tarea de F2 propone eliminar la capa de
aplicación al agregar mTLS.

### Relación con `ServiceIdentityOptions.CertificateThumbprint`

F2-04 ya dejó un campo de contrato preparado y explícitamente sin implementar:
`ServiceIdentityOptions.CertificateThumbprint` (autenticación de **cliente OAuth2** por certificado —
RFC 8705 "OAuth 2.0 Mutual-TLS Client Authentication", o `private_key_jwt`, RFC 7523 — en vez de
`client_secret`). Eso es un caso particular de uso de un certificado de workload dentro de la capa de
*aplicación* (reemplaza el secreto compartido del token endpoint por prueba de posesión de una clave
privada), no lo mismo que mTLS de *transporte* entre dos servicios cualesquiera de este contrato — pero
ambos comparten la misma fuente de identidad de workload (el certificado de servicio emitido por el
mecanismo de la sección 2). Cuando se implemente RFC 8705 (fuera de alcance de F2-14, requiere que el IdP
de referencia lo soporte y que exista al menos un consumidor real), reutilizará el mismo certificado que
este contrato exige para mTLS de transporte, en vez de aprovisionar dos identidades de certificado
distintas por workload.

## 2. Provisión de identidad y certificados

**Candidato de referencia propuesto (no adoptado — ver ADR 0015): SPIFFE/SPIRE.**

Requisitos que cualquier mecanismo elegido debe cumplir (SPIFFE/SPIRE, PKI interna o `cert-manager` —
alternativas de la ADR 0015):

1. **Un certificado por workload, no compartido entre instancias de servicios distintos.** Cada servicio
   extraído recibe su propia identidad (SPIFFE ID del estilo `spiffe://bitcode.local/ns/<servicio>` si se
   adopta SPIFFE/SPIRE, o un Common Name/SAN equivalente en una PKI interna) — nunca un certificado
   comodín compartido entre servicios distintos.
2. **Certificados de corta vida.** Vigencia recomendada de referencia: horas, no meses — minimiza el
   impacto de un certificado comprometido y hace la rotación automática (sección 3) una operación
   frecuente y probada, no un evento raro y riesgoso.
3. **La clave privada nunca se distribuye fuera del proceso/nodo que la usa.** Se genera localmente (o la
   entrega el agente local de SPIRE) y nunca viaja por red ni se guarda en `ISecretProvider` (F2-12) como
   si fuera un secreto estático — a diferencia de un `client_secret` OAuth2, una clave privada de mTLS de
   corta vida no es un secreto que un operador humano deba poder leer o rotar manualmente.
4. **La cadena de confianza (CA raíz/intermedia) es la única pieza estática y de larga vida del sistema**,
   y su compromiso es catastrófico (invalida la confianza en todos los certificados de workload emitidos)
   — su protección (HSM o equivalente, acceso restringido) es responsabilidad operativa explícita de quien
   opere el mecanismo elegido, no cubierta por el framework.

## 3. Rotación sin downtime

1. El certificado de un workload se renueva **antes** de su expiración (igual principio que
   `docs/politica-criptografica.md` sección 3.4 aplica a las claves de cifrado: rotar sin depender de un
   reinicio del proceso).
2. Durante la ventana de renovación, el servidor TLS de un servicio debe aceptar **tanto el certificado
   anterior (todavía vigente) como el nuevo** simultáneamente — nunca debe existir un instante en que un
   cliente que aún presenta el certificado anterior sea rechazado antes de que ese certificado expire
   naturalmente.
3. La lista de CA de confianza (para validar certificados entrantes, sección 4) se actualiza de forma
   independiente de la rotación de cada certificado de workload individual — un cambio de CA raíz/
   intermedia es un evento separado, más disruptivo, con su propio runbook (ver F9-10, "Operación...
   runbooks").
4. Igual que `AesGcmEncryptionProviderTests` prueba explícitamente rotación sin interrumpir descifrado
   (F2-13), la Definition of Done de F9-05 para un servicio extraído concreto debe incluir una prueba
   equivalente: renovar el certificado de un workload en caliente sin que las conexiones mTLS existentes
   ni las nuevas fallen.

## 4. Validación mutua (cliente y servidor)

mTLS exige verificación **en ambos sentidos** — ninguno de los dos lados confía en el otro sin verificar:

- **El servidor valida el certificado de cliente entrante:** rechaza cualquier conexión sin certificado de
  cliente, con un certificado expirado, revocado, o emitido por una CA fuera de la cadena de confianza del
  sistema. Requiere `ClientCertificateMode.RequireCertificate` (o equivalente del stack de servidor
  elegido) — nunca `AllowCertificate` como default en un servicio extraído (`AllowCertificate` acepta
  conexiones sin certificado, rompiendo la garantía "todo tráfico servicio-a-servicio está autenticado
  mutuamente").
- **El cliente valida el certificado de servidor:** nunca deshabilita la validación del certificado del
  servidor (equivalente a `ServerCertificateCustomValidationCallback = (_, _, _, _) => true` en
  `HttpClientHandler`/`SocketsHttpHandler` de .NET) — esa práctica queda **explícitamente prohibida** en
  cualquier código que implemente este contrato, incluso "temporalmente" o "solo en desarrollo", salvo un
  entorno de pruebas aisladas (Testcontainers) que use una CA de prueba propia, nunca deshabilitando la
  validación por completo.
- **Revocación:** el mecanismo de emisión elegido (sección 2) debe soportar invalidar un certificado de
  workload comprometido antes de su expiración natural (CRL/OCSP en una PKI tradicional; en SPIFFE/SPIRE,
  la revocación efectiva es "dejar de re-emitir" dado que los certificados son de corta vida — ver sección
  2.2). El contrato exige que exista *algún* mecanismo de invalidación anticipada, sin fijar cuál hasta que
  la ADR 0015 se resuelva.

## 5. Interacción con el gateway (YARP, ADR 0007)

Dos topologías válidas, a decidir explícitamente en F9-06 (Routing) para cada servicio extraído concreto —
este contrato no impone una sobre la otra:

- **TLS termination en el borde + mTLS interno re-originado:** el gateway termina TLS del tráfico externo
  (cliente↔gateway) y abre una conexión mTLS propia hacia el servicio interno, presentando su propio
  certificado de workload. Más simple de operar (el gateway concentra la gestión de certificados de
  cliente externos), pero el gateway pasa a ser parte de la superficie de confianza mTLS.
- **mTLS de extremo a extremo (passthrough):** el gateway no termina TLS, solo enruta a nivel de
  conexión — el cliente original y el servicio final se autentican mutuamente sin que el gateway participe
  de la validación de certificados. Requiere que YARP se configure en modo passthrough TCP/TLS, no como
  proxy HTTP terminador — a evaluar cuando exista el primer piloto de extracción (F9-01).

## 6. Matriz de aplicabilidad (resumen)

| Escenario | ¿Aplica mTLS de este contrato? |
|---|---|
| Llamadas entre módulos dentro del mismo proceso (monolito modular actual) | No aplica — no hay red de por medio. |
| Llamada HTTP saliente de un workload a otra API del framework hoy (F2-04, `IServiceTokenProvider`) | No obligatorio hoy; sigue autenticándose con OAuth2 Client Credentials. mTLS es una capa adicional que se habilitará junto con la extracción real de un servicio, no antes. |
| BFF → API protegida vía el proxy YARP (F2-03) | No aplica todavía — mismo motivo: no hay dos servicios operados de forma independiente, es un despliegue único. |
| Servicio extraído (Fase 9) llamando a otro servicio extraído o al monolito restante | **Obligatorio** (Plan Maestro sección 8.3, "mTLS entre servicios extraídos: Obligatorio") — Definition of Ready de F9-05/F9-06 sobre este documento. |

## Fuera de alcance de F2-14

- **Elección del mecanismo concreto de emisión de certificados** (SPIFFE/SPIRE, PKI interna, `cert-manager`)
  — `Proposed` en ADR 0015, pendiente de aprobación humana (Plan Maestro sección 13) y de que exista un
  primer piloto real de extracción (F9-01).
- **Código de infraestructura** (`IServiceCertificateProvider`, configuración de `SocketsHttpHandler`/
  Kestrel para presentar/validar certificados de cliente) — deliberadamente no agregado en esta tarea: no
  existe hoy un segundo servicio contra el cual verificarlo con un test de integración real, y construirlo
  sin ese caso de uso arriesga una interfaz equivocada para cuando la Fase 9 lo necesite de verdad. Ver
  ADR 0015, "Decisión", punto 3.
- **Implementación de RFC 8705 (`ServiceIdentityOptions.CertificateThumbprint`)** — sigue fallando
  explícitamente (`ServiceIdentity.CertificateAuthenticationNotSupported`) como ya lo dejó F2-04; este
  contrato documenta la relación conceptual (sección 1) pero no la implementa.
- **Auditoría de conexiones mTLS** — Épica F2-D (Auditoría inmutable), tarea separada.

## Referencias

- [`docs/adr/0015-mtls-contrato-operativo-servicio-a-servicio.md`](adr/0015-mtls-contrato-operativo-servicio-a-servicio.md) — decisión y estado `Proposed` del mecanismo de emisión.
- [`docs/adr/0004-identidad-idp-oidc-oauth2.md`](adr/0004-identidad-idp-oidc-oauth2.md) — F2-04, identidad de servicio OAuth2 Client Credentials con la que este contrato convive.
- [`docs/guia-oidc-adapter.md`](guia-oidc-adapter.md) — sección "F2-04", `ServiceIdentityOptions.CertificateThumbprint`.
- [`docs/politica-criptografica.md`](politica-criptografica.md) — F2-13, sección 3.4 (rotación sin downtime), mismo principio aplicado acá a certificados en vez de claves de cifrado.
- [`docs/adr/0007-gateway-yarp.md`](adr/0007-gateway-yarp.md) — YARP, relevante para la sección 5 (topología de terminación TLS).
- [`docs/plan-maestro-bitcode-ia.md`](plan-maestro-bitcode-ia.md) — Fase 9 (Capacidad de extracción de microservicios), sección 8.3 (mTLS obligatorio entre servicios extraídos).
