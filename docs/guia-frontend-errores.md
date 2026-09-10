# Guía — Errores / ProblemDetails / correlation id (F7-06, Fase 7 — Plataforma Angular empresarial)

> Tarea de origen: F7-06 (Errores) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md). Alcance:
> modelo tipado de `ProblemDetails`, interceptor HTTP centralizado de `@bitcode/core` y un servicio que
> traduce cualquier error HTTP a un mensaje consistente ("error experience"). No incluye un componente
> visual elaborado (toast/diálogo de error) — eso puede refinarse en `@bitcode/ui` en el futuro — ni i18n
> real de los mensajes (F7-11).

## 1. Dónde vive cada pieza

| Pieza | Ubicación |
|---|---|
| `ProblemDetails` / `ValidationProblemDetails` (modelo) | `frontend/packages/core/src/lib/errors/problem-details.model.ts` |
| `BitcodeUiError` / `BitcodeErrorKind` / catálogo de mensajes | `frontend/packages/core/src/lib/errors/bitcode-error.model.ts` |
| `BitcodeErrorExperienceService` | `frontend/packages/core/src/lib/errors/error-experience.service.ts` |
| `BitcodeHttpError` (envoltorio propagado por el interceptor) | `frontend/packages/core/src/lib/errors/bitcode-http-error.ts` |
| `bitcodeErrorInterceptor` | `frontend/packages/core/src/lib/errors/error.interceptor.ts` |
| Servidor HTTP real de prueba (`ProblemDetailsTestServer`) | `frontend/packages/core/src/lib/testing/problem-details-test-server.ts` |

Todo vive en `@bitcode/core` ("Configuración, errores, logging, HTTP y convenciones", Plan Maestro,
sección Fase 7) — es el paquete correcto según la tabla de paquetes objetivo de la fase, no uno nuevo.

## 2. La forma REAL de `ProblemDetails` que produce hoy el backend (verificado, no asumido)

Se leyó el código real del backend (`Shared.Infrastructure.Web/Results/ResultExtensions.cs`,
`Shared.Infrastructure.Web/Exceptions/GlobalExceptionHandler.cs`, `Shared.Infrastructure.Web/
WebServiceCollectionExtensions.cs`) antes de diseñar el modelo. Hay **tres formas distintas**, no una
sola:

1. **`Result.Failure` de negocio, cualquier `ErrorType` salvo `Validation`** (`ResultExtensions.
   ToProblemDetails`, vía `TypedResults.Problem`): ProblemDetails estándar de ASP.NET Core —
   `{ type, title, status, detail, instance }`. `title` es el CÓDIGO del error de dominio (p. ej.
   `"Producto.NoEncontrado"`), no un texto pensado para un usuario final.
2. **`ValidationError` (FluentValidation, 400)** (`TypedResults.ValidationProblem`): lo anterior más
   `errors: Record<string, string[]>`.
3. **Excepción no controlada (`GlobalExceptionHandler`, siempre 500)**: forma DISTINTA y NO estándar —
   `{ type, title, status, traceId }`, sin `detail` ni `instance`. `traceId` es
   `Activity.Current?.Id ?? HttpContext.TraceIdentifier`.

**Hallazgo honesto (no un supuesto de diseño):** el backend hoy **no** agrega ningún correlation id a las
formas (1)/(2) — `AddSharedExceptionHandling` llama `services.AddProblemDetails()` **sin**
`CustomizeProblemDetails`, y `AddSharedObservability` (`Shared.Infrastructure.Observability`) configura
`AddAspNetCoreInstrumentation()` para trazas/métricas salientes al collector OTLP, pero no inyecta ningún
header de respuesta (`traceparent`/`X-Correlation-Id`) hacia el cliente. El único campo parecido a un
correlation id que el backend expone hoy, de forma verificable, es el `traceId` de la forma (3) —
únicamente para un 500 no controlado. Para un `Result.Failure` de negocio (400/404/409/etc.), **no hay
ningún dato de correlación disponible en la respuesta HTTP hoy**.

`ProblemDetails` (el modelo TypeScript) documenta esto explícitamente y modela `traceId` como opcional y
el resto de las extensiones como un índice abierto (`[extension: string]: unknown`) — cuando el backend
incorpore un correlation id consistente (extensión `correlationId`, o un header como `X-Correlation-Id`),
`BitcodeErrorExperienceService.extractCorrelationId` ya sabe buscarlo en esos lugares sin cambiar de
forma; hoy simplemente no lo encuentra en la mayoría de los casos.

## 3. `BitcodeErrorExperienceService`: el único lugar que decide el mensaje

`fromHttpError(error: unknown): BitcodeUiError` es la única función que traduce un error crudo a algo
mostrable. Separa deliberadamente:

- `userMessage`: SIEMPRE del catálogo de mensajes por `kind` (`BITCODE_ERROR_MESSAGES`/
  `DEFAULT_BITCODE_ERROR_MESSAGES`), **nunca** el `detail`/`title` crudo del backend — ese texto puede
  contener información técnica interna (p. ej. un código de error de dominio, un mensaje de excepción).
- `technicalDetail`: el `detail`/`title` del backend, o el `message` genérico si no hay `ProblemDetails`
  parseable — pensado para un detalle técnico expandible/colapsable, no para el texto principal.
- `correlationId`: ver sección 2 — hoy casi siempre `undefined`, salvo un 500 no controlado. Debe
  mostrarse SIEMPRE de alguna forma en la UI (aunque sea "no disponible"), nunca omitirse en silencio.
- `fieldErrors`: sólo para `kind === 'validation'` (`ValidationProblemDetails.errors`).

`BitcodeErrorKind` (`'validation' | 'unauthorized' | 'forbidden' | 'not-found' | 'conflict' | 'server' |
'network' | 'unknown'`) es la clasificación estable que garantiza "mensajes consistentes" (criterio de
aceptación literal): el mapeo `status → kind → mensaje` es el único lugar donde se decide qué texto ve el
usuario para, por ejemplo, cualquier 404 de cualquier módulo — ningún componente debería tener su propio
`switch` de status codes.

Degradación sin romper (verificado con `ProblemDetailsTestServer`, no sólo declarado):

- `status === 0` (sin ciclo HTTP completo — CORS, DNS, conexión rechazada): `kind: 'network'`, sin
  intentar parsear ningún body.
- Body no-JSON (p. ej. HTML de un proxy/gateway intermedio devolviendo un 502 sin pasar por el backend):
  se atrapa el error de `JSON.parse` y se degrada a `problemDetails: undefined`, sin lanzar.
- Body vacío: mismo resultado, sin lanzar.
- Cualquier error que no sea `HttpErrorResponse` (excepción de programación en el pipeline de RxJS antes
  de llegar al backend): `kind: 'unknown'`, nunca se relanza sin clasificar.

## 4. `bitcodeErrorInterceptor` y orden recomendado con `@bitcode/auth`

`bitcodeErrorInterceptor` envuelve cualquier `HttpErrorResponse` en un `BitcodeHttpError` (extiende
`Error`; conserva el original en `.original` y el mapeo ya calculado en `.uiError`) y lo propaga — nunca
lo swallowea ni lo convierte en un valor exitoso.

**Orden recomendado de interceptors** (`provideHttpClient(withInterceptors([...]))`):

```ts
provideHttpClient(withInterceptors([bitcodeErrorInterceptor, bitcodeAuthInterceptor]))
```

En Angular, el array de `withInterceptors` define el orden de ida: el primer interceptor de la lista es
el más "externo" (ve la petición primero, y la respuesta/error ÚLTIMO, después de que los internos ya
corrieron). Con el orden de arriba:

1. `bitcodeAuthInterceptor` (`@bitcode/auth`, más interno) sigue siendo el ÚNICO responsable de la
   reacción específica a un 401 (marcar la sesión anónima y redirigir a login) — `bitcodeErrorInterceptor`
   no duplica ni reemplaza esa lógica. `bitcodeAuthInterceptor` relanza el error tal cual
   (`throwError(() => error)`), así que sigue recibiendo el `HttpErrorResponse` crudo, no un
   `BitcodeHttpError` ya envuelto (su chequeo `error instanceof HttpErrorResponse` seguiría funcionando
   igual si el orden se invirtiera por accidente, pero para no arriesgarlo el orden documentado y
   verificado es el de arriba).
2. `bitcodeErrorInterceptor` (más externo) recibe el error DESPUÉS de que `auth` ya reaccionó, lo mapea a
   un `BitcodeUiError` para CUALQUIER código (incluyendo 401/403) y es lo que finalmente ve la aplicación.

Verificado en `error.interceptor.spec.ts` con un doble mínimo de `bitcodeAuthInterceptor` (sin introducir
una dependencia real `@bitcode/core → @bitcode/auth`, que sería la dirección incorrecta): con el orden de
arriba, el doble de auth procesa el `HttpErrorResponse` crudo de un 401 real, y la aplicación recibe un
`BitcodeHttpError` con `uiError.kind === 'unauthorized'`.

`apps/shell/src/app/app.config.ts` no se modificó en esta tarea (fuera del alcance verificar/tocar
`@bitcode/auth`/`@bitcode/ui` salvo necesidad); un consumidor real que quiera activar ambos interceptors
debe registrar `provideHttpClient(withInterceptors([bitcodeErrorInterceptor, bitcodeAuthInterceptor]))` en
su propio `app.config.ts`, siguiendo el orden documentado arriba.

## 5. Cómo se probó (verificación real, no sólo declarada)

`ProblemDetailsTestServer` (`frontend/packages/core/src/lib/testing/problem-details-test-server.ts`) es
un servidor HTTP real (Node `http`, mismo patrón que `BffTestDouble` de `@bitcode/auth` y
`TestExternalHttpServer`/`TestReportingHttpServer` del backend .NET) que devuelve las tres formas reales
de la sección 2, más:

- Un 502 con body HTML (proxy intermedio sin `ProblemDetails`).
- Un 503 sin body en absoluto.
- Un 200 normal, para confirmar que una respuesta exitosa no pasa por ninguna rama de error.

`error.interceptor.spec.ts` ejercita el interceptor completo contra ese servidor (vía `HttpClient` real,
`withXhr()`, mismo patrón que las specs de `@bitcode/auth`), incluida la verificación de orden de
interceptors de la sección 4. `error-experience.service.spec.ts` cubre el servicio de mapeo de forma
unitaria (incluida la extracción de `correlationId` desde un header `traceparent` W3C, best-effort, ver
limitación de la sección 2).

Comandos ejecutados:

```bash
cd frontend
npx nx run core:test
npx nx run core:lint
npx nx run-many -t build test lint
```

Resultado: 16 tests nuevos pasando (`error-experience.service.spec.ts` + `error.interceptor.spec.ts`),
`core:lint` limpio, y `nx run-many -t build test lint` sigue pasando para los 8 proyectos del workspace
(25 tareas, sin regresiones en `auth`/`ui`/`grid`/`forms`/`workflow`/`documents`/`shell`).

## 6. Limitaciones y pendientes explícitos (fuera de alcance de F7-06)

- **Sin componente visual de error** (toast/diálogo/banner): esta tarea entrega el modelo/servicio que
  traduce un error a algo mostrable, no el componente de `@bitcode/ui` que lo renderice — queda pendiente
  para cuando `@bitcode/ui` tenga contenido real de componentes de feedback.
- **Sin correlation id real para la mayoría de los errores hoy** (ver sección 2, hallazgo honesto): sólo
  un 500 no controlado expone `traceId`. Habilitar un correlation id consistente para TODA respuesta de
  error (p. ej. vía `CustomizeProblemDetails` agregando `HttpContext.TraceIdentifier` a las extensiones, o
  un header `X-Correlation-Id` en el gateway) es trabajo de una tarea de backend futura, no de F7-06 —
  el modelo/servicio ya están preparados para consumirlo en cuanto exista.
- **`app.config.ts` de `apps/shell` no se actualizó**: no había ninguna llamada HTTP real de negocio en el
  shell que se beneficiara de registrar el interceptor en esta tarea (sólo el árbol de navegación
  estático); un consumidor real de un módulo de negocio (F7-07 en adelante) debe registrar
  `bitcodeErrorInterceptor` siguiendo el orden documentado en la sección 4.
- **Sin i18n real de los mensajes del catálogo**: resuelto en F7-11, ver
  [`docs/guia-frontend-i18n.md`](guia-frontend-i18n.md#5-catálogo-de-mensajes-de-error-por-locale-cierra-el-pendiente-de-f7-06)
  (`BITCODE_ERROR_MESSAGES_BY_LOCALE`, `provideBitcodeErrorMessagesForLocale`), con una limitación honesta
  documentada ahí: no es reactivo a un cambio de idioma en caliente.
- **Sin `depConstraints` de Nx** (heredado de F7-01/F7-02/F7-05): no cambia con esta tarea.
