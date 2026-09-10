# Guía — `@bitcode/forms` (F7-08, Fase 7 — Plataforma Angular empresarial)

> Tarea de origen: F7-08 (Forms) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md). Alcance:
> biblioteca de formularios dinámicos/tipados GENÉRICA (no conoce ninguna entidad de negocio concreta) con
> validación reactiva client-side consistente, mapeo de errores de servidor (`ValidationProblemDetails`,
> F7-06) a campo, y un componente de renderizado con layout de una sola columna. NO incluye wizards
> multi-step, campos condicionales/dependientes entre sí, arrays de sub-formularios repetibles, file
> upload, ni layout multi-columna -- ver sección 7 para el detalle honesto de lo que queda fuera de este
> primer corte.

## 1. Dónde vive cada pieza

| Pieza | Ubicación |
|---|---|
| `BitcodeDynamicForm<T>` (componente, selector `lib-bitcode-dynamic-form`) | `frontend/packages/forms/src/lib/dynamic-form/dynamic-form.ts` (+ `.html`/`.scss`) |
| `BitcodeFormFieldConfig` (config de campo) | `frontend/packages/forms/src/lib/models/form-field.model.ts` |
| `BitcodeFormSubmitHandler<T>` (contrato que implementa el consumidor para enviar) | `frontend/packages/forms/src/lib/models/form-submit-handler.model.ts` |
| `buildFieldValidators` (config declarativa → `ValidatorFn[]` reales de Angular) | `frontend/packages/forms/src/lib/validation/build-field-validators.ts` |
| `resolveFieldErrorMessage` / catálogo de mensajes (`BITCODE_FORM_FIELD_ERROR_MESSAGES`) | `frontend/packages/forms/src/lib/validation/field-error-messages.ts` |
| `applyServerFieldErrors` / `defaultFormFieldNameResolver` | `frontend/packages/forms/src/lib/validation/apply-server-field-errors.ts` |

Todo lo público se exporta desde `frontend/packages/forms/src/index.ts` (`@bitcode/forms`).

## 2. Este componente NO crea el `FormGroup` -- lo renderiza

Mismo criterio que `BitcodeGridDataSource<T>`/`BitcodeGridColumn<T>` en `@bitcode/grid` (F7-07): el
consumidor sigue siendo dueño del `FormGroup` real (`ReactiveFormsModule`, tipado con `FormControl`/
`FormGroup` de Angular). `<lib-bitcode-dynamic-form>` recibe ese `FormGroup` YA CONSTRUIDO más un array de
`BitcodeFormFieldConfig[]` puramente descriptivo, y se ocupa de:

1. Renderizar el control correcto según `field.type` (`text`/`number`/`date`/`select`/`checkbox`/
   `textarea`, layout de una sola columna).
2. Mostrar el mensaje de error de validación client-side de forma consistente (sección 3).
3. Aplicar `readOnly`/oculto/deshabilitado por permiso (sección 4).
4. Gestionar el ciclo de envío (`idle`/`submitting`/`error`) si se provee un `submitHandler` (sección 5).

`buildFieldValidators(field.validators)` es una utilidad SEPARADA y opcional: quien arma el `FormGroup` a
mano puede usarla para derivar la lista de `ValidatorFn` de la misma config declarativa que después
alimenta al componente, evitando declarar la misma regla dos veces (una en `Validators.*` y otra en
`BitcodeFormFieldConfig.validators` sólo para el mensaje) -- pero no es obligatorio: un `FormGroup`
armado enteramente a mano con sus propios `Validators.*` funciona igual con el componente, siempre que
`FormControl.errors` use las claves estándar de Angular (`required`, `minlength`, etc.) o las de un
validador custom declarado en `field.validators.custom`.

```ts
import { Component, inject } from '@angular/core';
import { FormControl, FormGroup } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import {
  BitcodeDynamicForm,
  BitcodeFormFieldConfig,
  BitcodeFormSubmitHandler,
  buildFieldValidators,
} from '@bitcode/forms';
import { Observable } from 'rxjs';

interface CrearUsuarioForm {
  readonly nombre: string;
  readonly email: string;
  readonly activo: boolean;
}

const FIELDS: BitcodeFormFieldConfig[] = [
  { name: 'nombre', label: 'Nombre', validators: { required: true, minLength: 3 } },
  { name: 'email', label: 'Email', validators: { required: true, email: true } },
  { name: 'activo', label: 'Activo', type: 'checkbox' },
];

class CrearUsuarioSubmitHandler implements BitcodeFormSubmitHandler<CrearUsuarioForm> {
  constructor(private readonly http: HttpClient) {}
  submit(value: CrearUsuarioForm): Observable<unknown> {
    return this.http.post('/api/usuarios', value);
  }
}

@Component({
  selector: 'app-crear-usuario-page',
  imports: [BitcodeDynamicForm],
  template: `
    <lib-bitcode-dynamic-form
      [fields]="fields"
      [form]="form"
      [submitHandler]="submitHandler"
      submitLabel="Crear usuario"
      (submitted)="onCreated($event)"
    />
  `,
})
export class CrearUsuarioPage {
  private readonly http = inject(HttpClient);

  readonly fields = FIELDS;
  readonly form = new FormGroup({
    nombre: new FormControl('', buildFieldValidators(FIELDS[0].validators)),
    email: new FormControl('', buildFieldValidators(FIELDS[1].validators)),
    activo: new FormControl(false),
  });
  readonly submitHandler = new CrearUsuarioSubmitHandler(this.http);

  onCreated(value: CrearUsuarioForm): void {
    // navegar, refrescar una lista, etc.
  }
}
```

## 3. Validación client-side: un único mecanismo para todos los campos

`BitcodeFormFieldConfig.validators` es una config declarativa (`required`/`minLength`/`maxLength`/
`pattern`/`min`/`max`/`email`) que `buildFieldValidators` traduce 1:1 a `Validators.*` REALES de Angular --
no se reimplementa ninguna regla que Angular ya resuelve bien. `custom` cubre cualquier regla de negocio
que no entre en ese catálogo (`ValidatorFn` + `errorKey` + mensaje).

`resolveFieldErrorMessage(control, field, messages)` es el ÚNICO lugar que decide qué texto se muestra
para un error de campo (criterio de aceptación literal: "mismo mecanismo en todos los campos, no cada
consumidor inventando su propio mensaje"):

1. Si el error coincide con un `errorKey` de `field.validators.custom`, usa ESE mensaje (más específico).
2. Si no, busca la clave en el catálogo global (`BITCODE_FORM_FIELD_ERROR_MESSAGES`,
   `DEFAULT_BITCODE_FORM_FIELD_ERROR_MESSAGES` por defecto -- overridable con
   `provideBitcodeFormFieldErrorMessages`, mismo patrón que `provideBitcodeErrorMessages` de
   `@bitcode/core`/F7-06).
3. Si ninguno de los dos conoce la clave, usa un mensaje genérico de último recurso -- nunca se deja un
   error sin texto visible.

`BitcodeDynamicForm.fieldErrorMessage(field)` sólo devuelve un mensaje cuando el control es inválido Y ya
fue tocado (`dirty`/`touched`) -- evita mostrar errores antes de que el usuario interactúe con el campo,
salvo que `onSubmit()` haya marcado todo como touched al intentar enviar un formulario inválido (mismo
criterio en TODOS los campos, no decidido campo por campo).

## 4. Mapeo de errores de servidor a campo: la convención central de esta tarea

`applyServerFieldErrors(form, error, resolveFieldName?)` es el mecanismo pedido explícitamente por F7-08:
dado un `BitcodeHttpError`/`BitcodeUiError` con `kind === 'validation'` (F7-06, `fieldErrors:
Record<string, string[]>`), marca con `control.setErrors(...)` el `FormControl` correspondiente.

### 4.1 La forma REAL de `fieldErrors` (verificado en el backend, no asumido)

Se leyó `Shared.Application.Behaviors.ValidationBehavior` y `Shared.Infrastructure.Web.Results.
ResultExtensions.ToProblemDetails` antes de diseñar esto: la clave de `errors` es EXACTAMENTE
`FluentValidationFailure.PropertyName` (`Error.Validation(failure.PropertyName, failure.ErrorMessage)`,
agrupado por esa clave) -- el nombre de la propiedad C# validada, en PascalCase, tal cual FluentValidation
lo resuelve. Para una propiedad de un objeto anidado, es un path con puntos (p. ej. `"Direccion.Calle"`).

### 4.2 Convención de mapeo por defecto (`defaultFormFieldNameResolver`)

**No hay ninguna garantía automática de que esa clave coincida con el nombre del `FormControl` de
Angular** (convención habitual del workspace: camelCase, plano). El resolver por defecto aplica la
transformación más simple y predecible que cubre el caso común (propiedad de primer nivel, mismo nombre
salvo casing):

1. Si la clave tiene puntos, toma el ÚLTIMO segmento (`"Direccion.Calle"` → `"Calle"` -- una propiedad
   anidada colapsa a su nombre de hoja, porque este primer corte de `BitcodeDynamicForm` sólo soporta un
   `FormGroup` de un único nivel, sin `FormGroup` anidados).
2. Baja la primera letra a minúscula (`"Calle"` → `"calle"`, `"Nombre"` → `"nombre"`).

Esto cubre el caso más común (un DTO plano validado por FluentValidation, con un `FormGroup` de Angular
que usa exactamente los mismos nombres de propiedad en camelCase) sin inventar un mapeo mágico o una
dependencia de metadata adicional que el backend no expone hoy.

**Si el `FormGroup` del consumidor no sigue esa convención** (nombres de control que no coinciden ni con
esta transformación simple, o dos propiedades anidadas distintas que colapsarían al mismo nombre de hoja),
debe proveer su propio resolver:

```ts
applyServerFieldErrors(form, error, (backendFieldName) =>
  backendFieldName === 'Direccion.Calle' ? 'direccionCalle' : defaultFormFieldNameResolver(backendFieldName),
);
```

`BitcodeDynamicForm` acepta el mismo resolver vía el input `resolveServerFieldName` y lo usa
automáticamente cuando el `submitHandler` falla con un error de validación (ver sección 5).

### 4.3 Qué pasa con una clave que no matchea ningún control

`applyServerFieldErrors` NUNCA descarta en silencio un error de campo que no pudo mapear a un control: lo
devuelve en `unmatchedFieldErrors` (`Record<string, string[]>`) para que el consumidor decida qué hacer
(mostrarlo igual en el banner general, loguearlo como una discrepancia de contrato a corregir, etc.). El
mensaje GENERAL de envío (`BitcodeUiError.userMessage`, sección 5) se muestra siempre de todos modos,
independientemente de cuántos `fieldErrors` se hayan podido mapear.

### 4.4 Verificación real (no simulada)

`apply-server-field-errors.spec.ts` construye el `BitcodeHttpError` con el servicio REAL de F7-06
(`BitcodeErrorExperienceService` resuelto vía `TestBed`) a partir de un `HttpErrorResponse` 400 con la
forma real de `ValidationProblemDetails` (`{ errors: { Nombre: [...] } }`) -- no un `BitcodeUiError`
armado a mano simulando el mapeo. Casos cubiertos: mapeo de un campo, de varios campos, un campo sin
control correspondiente (`unmatchedFieldErrors`), un resolver custom para un path anidado, un error que no
es de validación (no-op), y un `BitcodeUiError` plano sin el envoltorio `BitcodeHttpError`.

## 5. Estado de envío (`idle`/`submitting`/`error`)

`BitcodeDynamicForm.status()` (`'idle' | 'submitting' | 'error'`) y `submitError()` (`BitcodeUiError |
null`) son señales de sólo lectura de facto. `onSubmit()`:

1. `formGroup.markAllAsTouched()` -- para que `fieldErrorMessage` muestre inmediatamente cualquier error
   client-side existente, aunque el usuario nunca haya tocado el campo.
2. Si `formGroup.invalid`, no llama a nada más (ni al `submitHandler`) -- la validación client-side sigue
   siendo la primera barrera, sin depender del backend para detectar un campo vacío obligatorio.
3. Sin `submitHandler` (modo "sólo validar"): emite `submitted` con `formGroup.getRawValue()` y termina --
   el consumidor arma su propio pedido HTTP fuera de este componente. Es un modo de uso válido, no un caso
   de error.
4. Con `submitHandler` (`BitcodeFormSubmitHandler<T>.submit(value): Observable<unknown>`, análogo a
   `BitcodeGridDataSource<T>.loadPage` de F7-07): `status` pasa a `'submitting'`; en éxito vuelve a
   `'idle'` y emite `submitted`; en error, aplica `applyServerFieldErrors` (sección 4) Y calcula el
   `BitcodeUiError` general con el MISMO mecanismo que `BitcodeGrid` (reutiliza `error.uiError` si es un
   `BitcodeHttpError`, o `BitcodeErrorExperienceService.fromHttpError` en cualquier otro caso) -- nunca un
   mensaje inventado en este componente, ni siquiera para el caso de validación (el mensaje general sigue
   siendo el del catálogo de F7-06, `"Revisá los datos ingresados..."`, no un texto propio de `@bitcode/
   forms`).

El banner de error general (`dynamic-form.html`) muestra `userMessage`, el `correlationId` (o "no
disponible", nunca se omite en silencio -- mismo criterio que `docs/guia-frontend-errores.md`/
`BitcodeGrid`) y el `technicalDetail` en un `<details>` colapsable.

`formGroup.getRawValue()` (no `.value`) se usa deliberadamente al armar el valor a enviar/emitir: incluye
los controles deshabilitados (`readOnly`, o deshabilitados por permiso vía `permissionEffect: 'disable'`,
sección 6) -- un campo de sólo lectura sigue viajando con su valor actual, en vez de desaparecer del
payload.

## 6. Campos ocultos/deshabilitados/de sólo lectura

Análogo a `BitcodeGridColumn.requiredPermissions` (F7-07), reutilizando **exactamente** el mismo mecanismo
(`hasRequiredPermissions`/`@bitcode/auth`, F7-04) -- `BitcodeSessionService` se inyecta de forma
**opcional**: un formulario que no necesita ocultar/deshabilitar campos por permiso no está obligado a
tener `@bitcode/auth` provisto.

| Campo de config | Efecto |
|---|---|
| `hidden: true` | El campo no se renderiza NUNCA, independientemente de la sesión. |
| `readOnly: true` | El `FormControl` real se deshabilita (`control.disable()`) SIEMPRE, independientemente de permisos. |
| `requiredPermissions` + `permissionEffect: 'hide'` (default) | Sin el permiso, el campo no se renderiza (igual que `hidden`, pero condicionado a la sesión). |
| `requiredPermissions` + `permissionEffect: 'disable'` | Sin el permiso, el campo se renderiza pero el `FormControl` se deshabilita; con el permiso, se habilita. |

La sincronización de `enabled`/`disabled` ocurre en un `effect()` del componente que recorre `fields()` y
llama `control.disable({ emitEvent: false })`/`control.enable({ emitEvent: false })` sobre el `FormGroup`
REAL del consumidor -- es la única forma de que `Validators`/`form.invalid`/`form.value` (no
`getRawValue()`) respeten esas reglas declarativas. Esto significa que `BitcodeDynamicForm` MUTA controles
que no creó; se documenta explícitamente porque es una decisión de diseño con una consecuencia observable
(un componente externo que también observe ese mismo `FormGroup` ve los cambios de `enabled`/`disabled`).

## 7. Limitaciones y pendientes explícitos (fuera de alcance de F7-08)

- **Layout de una sola columna únicamente:** `dynamic-form.html` renderiza los campos en un `<form>` con
  `display: flex; flex-direction: column` -- no hay ningún mecanismo de grillas/columnas múltiples ni de
  agrupación visual de campos relacionados (fieldsets). Queda para una tarea posterior si se necesita.
- **Sin campos condicionales/dependientes entre sí** (mostrar/ocultar u obligar un campo según el valor de
  otro): no implementado. `BitcodeFormFieldConfig` es estático por campo; una dependencia entre campos
  hoy sólo puede resolverse fuera de este componente (el consumidor observa `form.valueChanges` y
  reconstruye su propio array de `fields` reactivamente si hace falta, pero no hay soporte de primera
  clase).
- **Sin arrays de sub-formularios repetibles** (`FormArray` de líneas, p. ej. "ítems de una orden"): no
  implementado -- un caso de uso real y común en formularios empresariales, pero significativamente más
  complejo (agregar/quitar filas, validación por fila) que este primer corte.
- **Sin wizards multi-step:** no implementado -- `BitcodeDynamicForm` renderiza siempre el `FormGroup`
  completo de una vez.
- **Sin file upload:** el tipo `BitcodeFormFieldType` no incluye `'file'` -- queda relacionado con
  `@bitcode/documents` (F7-10), que maneja carga/progreso/versiones de archivos de forma dedicada.
- **Sin máscaras de entrada** (p. ej. formato de CUIT/teléfono mientras se tipea): el criterio de
  aceptación de la fila del backlog menciona "máscaras" explícitamente, pero no se implementó una librería
  de máscaras de input en este primer corte -- un campo con ese requisito hoy debe resolverse con un
  validador `pattern`/`custom` (que valida el formato final) más, si se necesita el efecto visual de
  máscara mientras se tipea, una directiva de terceros o propia aplicada por el consumidor sobre el
  `<input>` renderizado (el componente no impide esa composición, pero no la provee).
- **Sin autocomplete/combo asíncrono** (`type: 'select'` sólo soporta una lista de opciones ya cargada en
  memoria, `BitcodeFormFieldOption[]`): un combo que busque contra el backend a medida que se tipea
  (equivalente al filtro server-side de `@bitcode/grid`) no está cubierto.
- **Sin accesibilidad auditada formalmente** (F7-12): se usaron atributos básicos (`role="alert"` para
  errores, `aria-invalid`, `<label for>` asociado a cada control) por buena práctica, pero no se corrió
  ninguna herramienta de auditoría (axe, Lighthouse).
- **Sin tema oscuro verificado visualmente:** los estilos usan `--bc-*` (F7-02, mismo criterio que
  `@bitcode/grid`), pero no se hizo una verificación visual manual en modo oscuro.
- **La convención de mapeo de `fieldErrors` (sección 4.2) es una heurística, no una garantía de
  contrato:** si en el futuro el backend expone una forma más rica de `ValidationProblemDetails` (p. ej.
  un `errorCode` estable independiente de `PropertyName`, o resuelve nombres de campo pensados para un
  cliente), el resolver por defecto puede simplificarse o eliminarse -- hoy es la mejor aproximación
  disponible sin inventar una garantía que el backend no ofrece.

## 8. Cómo se probó

Vitest + `TestBed`/`ReactiveFormsModule` REALES (no simulados), mismo runner que el resto del workspace:

- `src/lib/validation/build-field-validators.spec.ts`: cada validador estándar (`required`/`minLength`/
  `maxLength`/`pattern`/`min`/`max`/`email`) ejercitado contra un `FormControl` real de Angular, más un
  validador custom combinado con uno estándar.
- `src/lib/validation/field-error-messages.spec.ts`: resolución de mensajes contra errores REALES que
  produce Angular (`minlength`/`min` interpolando el valor real devuelto, no un texto fijo), prioridad de
  un mensaje custom de campo sobre el catálogo global, y fallback genérico para una clave desconocida.
- `src/lib/validation/apply-server-field-errors.spec.ts` (8 casos): ver sección 4.4.
- `src/lib/dynamic-form/dynamic-form.spec.ts` (9 casos), contra un host component con un `FormGroup` real:
  - Envío válido sin `submitHandler` emite `submitted` (validación real de Angular, no simulada).
  - Envío inválido no emite nada y marca todos los controles como touched.
  - Mensajes de error client-side en vivo a medida que cambia el valor de un campo.
  - Ciclo completo `submitting` → `idle` con un `submitHandler` real (`Subject` controlado por el test).
  - Un 400 `ValidationProblemDetails` real del `submitHandler` aplica el error al `FormControl` correcto
    Y muestra el mensaje general sin volver a mapearlo.
  - Un error no-validación (500) del `submitHandler` sólo afecta el mensaje general, ningún control.
  - Campo oculto por permiso (`permissionEffect: 'hide'`, default) reactivo a cambios de sesión.
  - Campo deshabilitado por permiso (`permissionEffect: 'disable'`) reactivo a cambios de sesión.
  - Campo `readOnly` siempre deshabilitado, independientemente de la sesión.

Comandos ejecutados:

```bash
cd frontend
npx nx run forms:test --skip-nx-cache
npx nx run forms:lint --skip-nx-cache
npx nx run forms:build --skip-nx-cache
npx nx run-many -t build test lint --skip-nx-cache
```

Resultado: 30 tests nuevos pasando en `forms` (7 + 6 + 8 + 9), `forms:lint`/`forms:build` limpios, y los 8
proyectos del workspace (`core`, `auth`, `ui`, `grid`, `forms`, `workflow`, `documents`, `shell`) siguen
pasando `build`/`test`/`lint` sin regresiones (25 tareas totales).
