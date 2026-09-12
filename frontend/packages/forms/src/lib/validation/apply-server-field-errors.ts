import { FormGroup } from '@angular/forms';
import { BitcodeHttpError, BitcodeUiError } from '@bitcode/core';
import { SERVER_FIELD_ERROR_KEY } from './field-error-messages';

/**
 * Traduce la clave de `BitcodeUiError.fieldErrors` (nombre de campo del BACKEND) al nombre del
 * `FormControl` correspondiente dentro del `FormGroup` del formulario. Recibe una función en vez de un
 * mapa fijo porque la relación entre ambos nombres depende de cómo el consumidor arma su `FormGroup`, no
 * es algo que esta librería pueda inferir de forma genérica y confiable -- ver la convención por defecto
 * abajo y la sección de convención de nombres en `docs/guia-frontend-forms.md`.
 */
export type BitcodeFormFieldNameResolver = (backendFieldName: string) => string;

/**
 * Convención por defecto, verificada contra el backend real (`Shared.Application.Behaviors.
 * ValidationBehavior`, F2): la clave de `errors` es EXACTAMENTE `FluentValidationFailure.PropertyName`
 * (PascalCase, el nombre de la propiedad C# tal cual -- para propiedades anidadas, un path con puntos,
 * p. ej. `"Direccion.Calle"`).
 *
 * NO hay ninguna garantía automática de que ese nombre coincida con el nombre del `FormControl` de
 * Angular (convención habitual del workspace: camelCase, plano, sin anidar -- este primer corte de
 * `<lib-bitcode-dynamic-form>` sólo soporta un `FormGroup` de un único nivel, ver
 * `docs/guia-frontend-forms.md`). El resolver por defecto aplica la conversión más simple y predecible que
 * cubre el caso común (propiedad de primer nivel, mismo nombre salvo casing):
 *
 * 1. Toma el ÚLTIMO segmento de un path con puntos (`"Direccion.Calle"` -> `"Calle"`) -- una propiedad
 *    anidada colapsa a su nombre de hoja, ya que este primer corte no arma `FormGroup` anidados.
 * 2. Convierte la primera letra a minúscula (`"Calle"` -> `"calle"`, `"Nombre"` -> `"nombre"`).
 *
 * Si el `FormGroup` del consumidor no sigue esa convención (nombres de control que no coinciden ni
 * siquiera con esta transformación simple, o dos propiedades anidadas distintas que colapsan al mismo
 * nombre de hoja), debe proveer su propio `BitcodeFormFieldNameResolver` -- ver
 * `applyServerFieldErrors(form, error, resolver)`.
 */
export const defaultFormFieldNameResolver: BitcodeFormFieldNameResolver = (backendFieldName) => {
  const lastSegment = backendFieldName.includes('.')
    ? (backendFieldName.split('.').pop() ?? backendFieldName)
    : backendFieldName;
  return lastSegment.length === 0 ? lastSegment : lastSegment.charAt(0).toLowerCase() + lastSegment.slice(1);
};

export interface BitcodeFormServerErrorsResult {
  /** Nombres de `FormControl` (ya resueltos) a los que se les aplicó `setErrors`. */
  readonly appliedFieldNames: readonly string[];
  /** Errores del backend cuya clave resuelta NO coincide con ningún control del `form` -- se conservan
   * para que el consumidor decida qué hacer (p. ej. mostrarlos igual en el banner general de error, o
   * loguearlos como una discrepancia de contrato a corregir). Nunca se descartan en silencio. */
  readonly unmatchedFieldErrors: Readonly<Record<string, readonly string[]>>;
}

function extractValidationUiError(error: unknown): BitcodeUiError | null {
  if (error instanceof BitcodeHttpError) {
    return error.uiError.kind === 'validation' ? error.uiError : null;
  }
  if (isBitcodeUiError(error)) {
    return error.kind === 'validation' ? error : null;
  }
  return null;
}

function isBitcodeUiError(value: unknown): value is BitcodeUiError {
  return typeof value === 'object' && value !== null && 'kind' in value && 'userMessage' in value;
}

/**
 * Aplica los `fieldErrors` de un `BitcodeUiError`/`BitcodeHttpError` (kind `'validation'`, F7-06) como
 * `setErrors()` sobre los `FormControl` del `form` que coincidan según `resolveFieldName` -- el mecanismo
 * central pedido por F7-08 para mapear un 400 `ValidationProblemDetails` del backend a errores por campo
 * del formulario.
 *
 * Cualquier otro `error` (no validación, o sin `fieldErrors`) es un no-op que devuelve listas vacías -- el
 * llamador sigue siendo responsable de mostrar el mensaje general de envío (`BitcodeUiError.userMessage`,
 * ver `dynamic-form.ts`), esta función sólo se ocupa de los errores POR CAMPO.
 */
export function applyServerFieldErrors(
  form: FormGroup,
  error: unknown,
  resolveFieldName: BitcodeFormFieldNameResolver = defaultFormFieldNameResolver,
): BitcodeFormServerErrorsResult {
  const uiError = extractValidationUiError(error);
  if (!uiError?.fieldErrors) {
    return { appliedFieldNames: [], unmatchedFieldErrors: {} };
  }

  const appliedFieldNames: string[] = [];
  const unmatchedFieldErrors: Record<string, readonly string[]> = {};

  for (const [backendFieldName, messages] of Object.entries(uiError.fieldErrors)) {
    const controlName = resolveFieldName(backendFieldName);
    const control = controlName ? form.get(controlName) : null;

    if (!control) {
      unmatchedFieldErrors[backendFieldName] = messages;
      continue;
    }

    control.setErrors({
      ...control.errors,
      [SERVER_FIELD_ERROR_KEY]: messages[0] ?? '',
    });
    control.markAsTouched();
    appliedFieldNames.push(controlName);
  }

  return { appliedFieldNames, unmatchedFieldErrors };
}
