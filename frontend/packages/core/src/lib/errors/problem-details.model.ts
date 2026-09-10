/**
 * Modelo tipado de `ProblemDetails` (RFC 7807) tal como lo produce HOY el backend de BitCode
 * (`Shared.Infrastructure.Web/Results/ResultExtensions.cs`, `Shared.Infrastructure.Web/Exceptions/
 * GlobalExceptionHandler.cs`). Documentado con la forma REAL verificada en el código del backend, no la
 * forma "ideal" de la RFC -- ver notas de cada campo.
 *
 * Formas reales verificadas (F7-06):
 *
 * 1. Un `Result.Failure` de negocio (`ResultExtensions.ToProblemDetails`, cualquier `ErrorType` que no sea
 *    `Validation`) produce el `ProblemDetails` estándar de ASP.NET Core (`TypedResults.Problem`):
 *    `{ type, title, status, detail, instance }`. `title` lleva el CÓDIGO del error de dominio (p. ej.
 *    `"Producto.NoEncontrado"`), no un texto pensado para mostrarse a un usuario final -- ver
 *    `BitcodeErrorExperienceService` para el mapeo a un mensaje consistente por `status`.
 * 2. Un `ValidationError` (`FluentValidation`, HTTP 400) produce un `ValidationProblemDetails`
 *    (`TypedResults.ValidationProblem`): además de los campos anteriores, `errors: Record<string,
 *    string[]>` (clave = código de campo, valor = descripciones).
 * 3. Una excepción NO controlada (`GlobalExceptionHandler`, siempre HTTP 500) produce una forma DISTINTA y
 *    NO estándar: `{ type, title, status, traceId }` -- sin `detail` ni `instance`, y con un `traceId`
 *    (identificador de diagnóstico interno, `Activity.Current?.Id` o `HttpContext.TraceIdentifier`) que
 *    NO es una extensión RFC 7807 declarada como tal, es simplemente una propiedad más del objeto JSON.
 *
 * Hoy el backend NO agrega ningún correlation id a la forma (1)/(2) (`AddProblemDetails()` se llama sin
 * `CustomizeProblemDetails`, y `AddSharedObservability` no inyecta ningún header de respuesta tipo
 * `traceparent`/`X-Correlation-Id`) -- confirmado leyendo el código, no asumido. Por eso este modelo
 * declara `traceId` como opcional y el resto de las extensiones como un índice abierto: cuando el backend
 * incorpore un correlation id consistente en todas las formas (extensión `traceId`/`correlationId`, o un
 * header de respuesta), el código de `BitcodeErrorExperienceService` que lo extrae no necesita cambiar de
 * forma, sólo empieza a encontrar el dato donde hoy encuentra `undefined`.
 */
export interface ProblemDetails {
  readonly type?: string;
  readonly title?: string;
  readonly status?: number;
  readonly detail?: string;
  readonly instance?: string;
  /**
   * Sólo presente hoy en la forma (3) de `GlobalExceptionHandler` (errores 500 no controlados). Es el
   * único campo de correlación que el backend expone de manera real y verificada a día de F7-06.
   */
  readonly traceId?: string;
  /** Cualquier otra extensión RFC 7807 (`extensions`) que un backend futuro agregue -- p. ej. un
   * `correlationId` explícito. Acceso defensivo, nunca asumido presente. */
  readonly [extension: string]: unknown;
}

/** Forma (2): `ValidationProblemDetails` de ASP.NET Core (`TypedResults.ValidationProblem`). */
export interface ValidationProblemDetails extends ProblemDetails {
  readonly errors: Readonly<Record<string, readonly string[]>>;
}

export function isProblemDetails(value: unknown): value is ProblemDetails {
  return (
    typeof value === 'object' &&
    value !== null &&
    ('title' in value || 'status' in value || 'type' in value || 'traceId' in value)
  );
}

export function isValidationProblemDetails(value: ProblemDetails): value is ValidationProblemDetails {
  return (
    'errors' in value &&
    typeof (value as { errors?: unknown }).errors === 'object' &&
    (value as { errors?: unknown }).errors !== null
  );
}
