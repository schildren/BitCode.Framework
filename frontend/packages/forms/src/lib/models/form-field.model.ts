import { ValidatorFn } from '@angular/forms';
import { BitcodePermissionMode } from '@bitcode/auth';

/**
 * Tipo de campo soportado por `<lib-bitcode-dynamic-form>` en este primer corte (F7-08). Cubre los tipos
 * de entrada de negocio más comunes -- ver `docs/guia-frontend-forms.md` sección de limitaciones para lo
 * que queda deliberadamente fuera (file upload, campos de rango dual, autocomplete asíncrono, etc.).
 */
export type BitcodeFormFieldType = 'text' | 'number' | 'date' | 'select' | 'checkbox' | 'textarea';

/** Opción de un campo `'select'`. `value` viaja tal cual al `FormControl` (no se fuerza a `string`). */
export interface BitcodeFormFieldOption {
  readonly value: unknown;
  readonly label: string;
}

/**
 * Validador custom asociado a un campo. Se agrega a la lista de `ValidatorFn` del control (junto con los
 * validadores estándar de `BitcodeFormFieldValidators`) y aporta su propio mensaje -- el catálogo global
 * de mensajes (`field-error-messages.ts`) no puede conocer de antemano la clave de error de un validador
 * de negocio específico de un campo.
 */
export interface BitcodeFormFieldCustomValidator {
  readonly validate: ValidatorFn;
  /** Clave que el validador escribe en `control.errors` (p. ej. `'cuitInvalido'`) -- debe coincidir
   * exactamente con la clave que devuelve `validate`. */
  readonly errorKey: string;
  /** Mensaje fijo, o función que recibe el valor de `control.errors[errorKey]` (lo que haya devuelto
   * `validate`) para construir un mensaje con datos del propio error. */
  readonly message: string | ((error: unknown) => string);
}

/**
 * Validadores declarativos de un campo. Los estándar (`required`/`minLength`/`maxLength`/`pattern`/
 * `min`/`max`/`email`) se traducen 1:1 a `Validators.*` de Angular (`build-field-validators.ts`) -- no se
 * reinventa ninguna validación que Angular ya resuelve bien. `custom` cubre cualquier regla de negocio que
 * no entre en ese catálogo estándar.
 */
export interface BitcodeFormFieldValidators {
  readonly required?: boolean;
  readonly minLength?: number;
  readonly maxLength?: number;
  readonly pattern?: string | RegExp;
  readonly min?: number;
  readonly max?: number;
  readonly email?: boolean;
  readonly custom?: readonly BitcodeFormFieldCustomValidator[];
}

/**
 * Qué pasa con un campo cuando el actor NO tiene `requiredPermissions`:
 *
 * - `'hide'` (default): el campo no se renderiza en absoluto (mismo criterio que
 *   `BitcodeGridColumn.requiredPermissions`/F7-07 -- "sin sesión resuelta" nunca se trata como
 *   autorizado, y sin el permiso el campo desaparece).
 * - `'disable'`: el campo se renderiza pero deshabilitado (`FormControl.disable()`) -- útil cuando el
 *   campo es relevante para el contexto igual (p. ej. mostrar un valor de sólo lectura a quien no puede
 *   editarlo), a diferencia de ocultarlo por completo.
 */
export type BitcodeFormFieldPermissionEffect = 'hide' | 'disable';

/**
 * Configuración tipada de un campo de `<lib-bitcode-dynamic-form>` (F7-08), análoga a
 * `BitcodeGridColumn<T>` (F7-07) y `BitcodeMenuItem` (F7-05): datos puros, sin acoplarse a ninguna entidad
 * de negocio concreta. `name` DEBE coincidir con el nombre del `FormControl` correspondiente dentro del
 * `FormGroup` que el consumidor arma (el componente no crea los controles, sólo los configura/renderiza --
 * ver `docs/guia-frontend-forms.md` sección 2).
 */
export interface BitcodeFormFieldConfig {
  readonly name: string;
  readonly label: string;
  /** Default `'text'`. */
  readonly type?: BitcodeFormFieldType;
  readonly placeholder?: string;
  /** Texto de ayuda mostrado siempre debajo del campo (independiente de los mensajes de error). */
  readonly hint?: string;
  /** Requerido para `type: 'select'`. Ignorado para el resto de los tipos. */
  readonly options?: readonly BitcodeFormFieldOption[];
  readonly validators?: BitcodeFormFieldValidators;
  /** Campo de sólo lectura SIEMPRE (independiente de permisos) -- el `FormControl` se deshabilita. */
  readonly readOnly?: boolean;
  /** Campo oculto SIEMPRE (independiente de permisos). Para ocultar/deshabilitar por permiso, usar
   * `requiredPermissions` + `permissionEffect`. */
  readonly hidden?: boolean;
  /** Permiso(s) requeridos para ver (o, según `permissionEffect`, editar) el campo -- mismo mecanismo que
   * `BitcodeGridColumn.requiredPermissions` (`hasRequiredPermissions`/`@bitcode/auth`, F7-04). Sin este
   * campo, el campo es visible/editable para cualquier sesión (incluida ninguna sesión resuelta). */
  readonly requiredPermissions?: string | readonly string[];
  readonly permissionMode?: BitcodePermissionMode;
  /** Default `'hide'`. */
  readonly permissionEffect?: BitcodeFormFieldPermissionEffect;
}
