import type { components } from './generated/sample-api.v1';
import type { ProblemDetails, ValidationProblemDetails } from '../errors/problem-details.model';

/**
 * Verificación en tiempo de COMPILACIÓN de que `ProblemDetails`/`ValidationProblemDetails`
 * (`errors/problem-details.model.ts`) siguen cubriendo, como mínimo, todos los campos que el contrato
 * OpenAPI real (`Sample.Api`, RFC 7807 vía `ResultExtensions.ToProblemDetails`) declara -- ver
 * `docs/guia-contratos-frontend.md` y `documents/src/lib/contracts/contract-consistency.ts` para la
 * justificación completa de por qué este paquete NO re-exporta directamente los tipos generados.
 *
 * A diferencia de `documents`/`workflow`, acá se verifica SUBSET (claves generadas ⊆ claves de mano), no
 * igualdad exacta: `ProblemDetails` declara a propósito un índice abierto (`[extension: string]:
 * unknown`) y `traceId` -- campos reales que el backend expone en la forma NO estándar de
 * `GlobalExceptionHandler` (errores 500) pero que ningún schema de componente OpenAPI declara, porque
 * esa forma nunca pasa por `.Produces`/`.ProducesProblem` (ver el comentario de la clase en
 * `problem-details.model.ts`).
 */
type Expect<T extends true> = T;

type IsSubsetOf<Generated, Hand> = [keyof Generated] extends [keyof Hand] ? true : false;

// eslint-disable-next-line @typescript-eslint/no-unused-vars
type _ProblemDetailsCubreElContratoGenerado = Expect<
  IsSubsetOf<components['schemas']['ProblemDetails'], ProblemDetails>
>;

// eslint-disable-next-line @typescript-eslint/no-unused-vars
type _ValidationProblemDetailsCubreElContratoGenerado = Expect<
  IsSubsetOf<components['schemas']['HttpValidationProblemDetails'], ValidationProblemDetails>
>;
