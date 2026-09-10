import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { AbstractControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { BitcodeSessionService, hasRequiredPermissions } from '@bitcode/auth';
import { BitcodeErrorExperienceService, BitcodeHttpError, BitcodeUiError } from '@bitcode/core';
import { BitcodeFormFieldConfig } from '../models/form-field.model';
import { BitcodeFormSubmitHandler } from '../models/form-submit-handler.model';
import {
  BITCODE_FORM_FIELD_ERROR_MESSAGES,
  BitcodeFormFieldErrorMessages,
  resolveFieldErrorMessage,
} from '../validation/field-error-messages';
import {
  applyServerFieldErrors,
  BitcodeFormFieldNameResolver,
  defaultFormFieldNameResolver,
} from '../validation/apply-server-field-errors';

export type BitcodeFormSubmitStatus = 'idle' | 'submitting' | 'error';

/**
 * Formulario dinámico empresarial genérico (F7-08). NO conoce ninguna entidad de negocio concreta -- el
 * consumidor arma su propio `FormGroup` tipado (con sus `FormControl` y validadores, si los arma a mano) o
 * usa `buildFieldValidators` (`@bitcode/forms`) para derivarlos de `BitcodeFormFieldConfig.validators`, y
 * este componente sólo RENDERIZA esos campos + aplica reglas declarativas (sólo lectura/oculto/
 * deshabilitado por permiso) + gestiona el ciclo de envío. Ver `docs/guia-frontend-forms.md` para el
 * detalle y las decisiones de diseño (en particular la convención de mapeo de errores de servidor a
 * campo).
 *
 * Reglas de diseño no negociables de esta tarea:
 *
 * - Validación reactiva SIEMPRE vía `Validators`/`ValidatorFn` reales de Angular sobre el `FormGroup` real
 *   del consumidor -- este componente nunca reimplementa una validación por su cuenta.
 * - El error de envío general SIEMPRE se muestra vía `BitcodeUiError` (`@bitcode/core`, F7-06) -- nunca un
 *   mensaje inventado en este componente (mismo criterio que `BitcodeGrid`, F7-07).
 * - Un 400 `ValidationProblemDetails` (`kind: 'validation'`, F7-06) además aplica sus `fieldErrors` sobre
 *   los `FormControl` correspondientes (`applyServerFieldErrors`), no sólo el mensaje general.
 * - Campos ocultos/deshabilitados por permiso reutilizan `hasRequiredPermissions`/`@bitcode/auth` (F7-04),
 *   igual que `BitcodeGridColumn.requiredPermissions` -- `@bitcode/auth` se inyecta de forma OPCIONAL.
 */
@Component({
  selector: 'lib-bitcode-dynamic-form',
  imports: [ReactiveFormsModule],
  templateUrl: './dynamic-form.html',
  styleUrl: './dynamic-form.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BitcodeDynamicForm<T = Record<string, unknown>> {
  private readonly session = inject(BitcodeSessionService, { optional: true });
  private readonly errorExperience = inject(BitcodeErrorExperienceService);
  private readonly defaultFieldErrorMessages = inject(BITCODE_FORM_FIELD_ERROR_MESSAGES);

  readonly fields = input.required<readonly BitcodeFormFieldConfig[]>();
  readonly form = input.required<FormGroup>();
  /** Sin `submitHandler`, `onSubmit()` sólo valida client-side y emite `submitted` con el valor -- el
   * consumidor arma su propio pedido HTTP fuera de este componente. Con `submitHandler`, este componente
   * gestiona el ciclo completo de envío (`status`/`submitError`), igual que `BitcodeGridDataSource` en
   * `BitcodeGrid` (F7-07). */
  readonly submitHandler = input<BitcodeFormSubmitHandler<T> | undefined>(undefined);
  readonly submitLabel = input<string>('Guardar');
  /** Override puntual del catálogo de mensajes de error de campo para ESTE formulario -- sin este input,
   * se usa `BITCODE_FORM_FIELD_ERROR_MESSAGES` (inyectable a nivel de aplicación). */
  readonly fieldErrorMessages = input<BitcodeFormFieldErrorMessages | undefined>(undefined);
  /** Ver convención por defecto y su justificación en `apply-server-field-errors.ts`. */
  readonly resolveServerFieldName = input<BitcodeFormFieldNameResolver>(defaultFormFieldNameResolver);

  readonly submitted = output<T>();

  readonly status = signal<BitcodeFormSubmitStatus>('idle');
  readonly submitError = signal<BitcodeUiError | null>(null);

  readonly visibleFields = computed(() => {
    const claims = this.session?.claims() ?? null;
    return this.fields().filter((field) => {
      if (field.hidden) {
        return false;
      }
      if (!field.requiredPermissions) {
        return true;
      }
      const allowed = hasRequiredPermissions(claims, field.requiredPermissions, field.permissionMode ?? 'all');
      // 'disable' (default distinto de 'hide'): el campo sigue visible aunque falte el permiso -- se
      // deshabilita en el `effect()` del constructor, no acá.
      return allowed || (field.permissionEffect ?? 'hide') !== 'hide';
    });
  });

  private readonly effectiveFieldErrorMessages = computed(
    () => this.fieldErrorMessages() ?? this.defaultFieldErrorMessages,
  );

  constructor() {
    // Sincroniza enabled/disabled de los controles REALES del `FormGroup` del consumidor según
    // `readOnly`/`requiredPermissions`+`permissionEffect: 'disable'` -- es la única forma de que
    // `Validators`/`form.invalid`/serialización respeten esas reglas declarativas (un control disabled
    // queda fuera de la validación y de `FormGroup.value`, aunque `getRawValue()` sigue exponiéndolo, ver
    // `onSubmit()`). Se re-ejecuta cada vez que cambian `fields()`/`form()`/los claims de sesión.
    effect(() => {
      const claims = this.session?.claims() ?? null;
      const formGroup = this.form();
      for (const field of this.fields()) {
        const control = formGroup.get(field.name);
        if (!control) {
          continue;
        }

        const disabledByPermission =
          !!field.requiredPermissions &&
          (field.permissionEffect ?? 'hide') === 'disable' &&
          !hasRequiredPermissions(claims, field.requiredPermissions, field.permissionMode ?? 'all');
        const shouldDisable = !!field.readOnly || disabledByPermission;

        if (shouldDisable && control.enabled) {
          control.disable({ emitEvent: false });
        } else if (!shouldDisable && control.disabled) {
          control.enable({ emitEvent: false });
        }
      }
    });
  }

  fieldId(field: BitcodeFormFieldConfig): string {
    return `bc-form-field-${field.name}`;
  }

  control(field: BitcodeFormFieldConfig): AbstractControl | null {
    return this.form().get(field.name);
  }

  /** Un error se muestra sólo si el usuario ya interactuó con el campo (`dirty`/`touched`) -- o si
   * `onSubmit()` marcó todo como touched al intentar enviar un formulario inválido. Mismo criterio en
   * TODOS los campos (mecanismo único, "errores claros y consistentes" -- criterio de aceptación literal
   * de F7-08), nunca decidido campo por campo. */
  showFieldError(field: BitcodeFormFieldConfig): boolean {
    const control = this.control(field);
    return !!control && control.invalid && (control.dirty || control.touched);
  }

  fieldErrorMessage(field: BitcodeFormFieldConfig): string | null {
    if (!this.showFieldError(field)) {
      return null;
    }
    return resolveFieldErrorMessage(this.control(field), field, this.effectiveFieldErrorMessages());
  }

  onSubmit(): void {
    if (this.status() === 'submitting') {
      return;
    }

    const formGroup = this.form();
    formGroup.markAllAsTouched();
    if (formGroup.invalid) {
      return;
    }

    const handler = this.submitHandler();
    if (!handler) {
      this.submitted.emit(formGroup.getRawValue() as T);
      return;
    }

    this.status.set('submitting');
    this.submitError.set(null);

    handler.submit(formGroup.getRawValue() as T).subscribe({
      next: () => {
        this.status.set('idle');
        this.submitted.emit(formGroup.getRawValue() as T);
      },
      error: (rawError: unknown) => {
        // Un 400 `ValidationProblemDetails` aplica sus errores POR CAMPO acá; el mensaje GENERAL (banner,
        // ver `dynamic-form.html`) se calcula por separado con `toUiError` y es siempre el mismo mecanismo
        // reutilizado de F7-06, nunca un texto propio de este componente.
        applyServerFieldErrors(formGroup, rawError, this.resolveServerFieldName());
        this.submitError.set(this.toUiError(rawError));
        this.status.set('error');
      },
    });
  }

  private toUiError(error: unknown): BitcodeUiError {
    if (error instanceof BitcodeHttpError) {
      return error.uiError;
    }
    return this.errorExperience.fromHttpError(error);
  }
}
