import { HttpErrorResponse } from '@angular/common/http';
import { Component, signal, viewChild } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormControl, FormGroup, Validators } from '@angular/forms';
import { BitcodeSessionService, BitcodeUserClaims } from '@bitcode/auth';
import { BitcodeErrorExperienceService, BitcodeHttpError } from '@bitcode/core';
import { Observable, Subject, throwError } from 'rxjs';
import { describe, expect, it } from 'vitest';
import { BitcodeFormFieldConfig } from '../models/form-field.model';
import { BitcodeFormSubmitHandler } from '../models/form-submit-handler.model';
import { BitcodeDynamicForm } from './dynamic-form';

interface UsuarioForm {
  readonly nombre: string;
  readonly email: string;
  readonly activo: boolean;
}

function buildFields(): BitcodeFormFieldConfig[] {
  return [
    {
      name: 'nombre',
      label: 'Nombre',
      validators: { required: true, minLength: 3 },
    },
    { name: 'email', label: 'Email', type: 'text', validators: { required: true, email: true } },
    { name: 'activo', label: 'Activo', type: 'checkbox' },
  ];
}

function buildFormGroup(): FormGroup {
  return new FormGroup({
    nombre: new FormControl('', [Validators.required, Validators.minLength(3)]),
    email: new FormControl('', [Validators.required, Validators.email]),
    activo: new FormControl(false),
  });
}

/** Doble mínimo de `BitcodeSessionService` -- mismo patrón que `grid.spec.ts`/
 * `has-permission.directive.spec.ts`. */
class FakeSessionService {
  readonly claims = signal<BitcodeUserClaims | null>(null);
}

@Component({
  standalone: true,
  imports: [BitcodeDynamicForm],
  template: `
    <lib-bitcode-dynamic-form
      [fields]="fields()"
      [form]="form()"
      [submitHandler]="submitHandler()"
      (submitted)="onSubmitted($event)"
    />
  `,
})
class HostComponent {
  readonly dynamicForm = viewChild.required(BitcodeDynamicForm<UsuarioForm>);
  readonly fields = signal<readonly BitcodeFormFieldConfig[]>(buildFields());
  readonly form = signal<FormGroup>(buildFormGroup());
  readonly submitHandler = signal<BitcodeFormSubmitHandler<UsuarioForm> | undefined>(undefined);
  readonly submittedValues: UsuarioForm[] = [];

  onSubmitted(value: UsuarioForm): void {
    this.submittedValues.push(value);
  }
}

function setup() {
  TestBed.configureTestingModule({});
  const fixture = TestBed.createComponent(HostComponent);
  fixture.detectChanges();
  return { fixture, host: fixture.componentInstance };
}

function buildValidationHttpError(errors: Record<string, string[]>): BitcodeHttpError {
  const experience = TestBed.inject(BitcodeErrorExperienceService);
  const httpError = new HttpErrorResponse({
    status: 400,
    error: {
      title: 'One or more validation errors occurred.',
      status: 400,
      errors,
    },
  });
  return new BitcodeHttpError(httpError, experience.fromHttpError(httpError));
}

describe('BitcodeDynamicForm (F7-08)', () => {
  it('sin submitHandler, un envío válido sólo emite `submitted` con el valor -- validación real, no simulada', () => {
    const { fixture, host } = setup();

    host.form().patchValue({ nombre: 'Ana', email: 'ana@ejemplo.com', activo: true });
    fixture.detectChanges();
    host.dynamicForm().onSubmit();

    expect(host.submittedValues).toEqual([{ nombre: 'Ana', email: 'ana@ejemplo.com', activo: true }]);
  });

  it('un envío con el FormGroup inválido NO emite `submitted` y marca todos los controles como touched', () => {
    const { fixture, host } = setup();

    expect(host.form().get('nombre')?.touched).toBe(false);
    host.dynamicForm().onSubmit();
    fixture.detectChanges();

    expect(host.submittedValues).toEqual([]);
    expect(host.form().get('nombre')?.touched).toBe(true);
    expect(host.form().get('email')?.touched).toBe(true);
  });

  it('muestra el mensaje de error del catálogo por defecto para un campo tocado e inválido (validación real de Angular)', () => {
    const { fixture, host } = setup();

    host.form().get('nombre')?.markAsTouched();
    fixture.detectChanges();

    const message = host.dynamicForm().fieldErrorMessage(buildFields()[0]);
    expect(message).toBe('Este campo es obligatorio.');

    host.form().get('nombre')?.setValue('ab');
    fixture.detectChanges();
    expect(host.dynamicForm().fieldErrorMessage(buildFields()[0])).toBe('Debe tener al menos 3 caracteres.');

    host.form().get('nombre')?.setValue('abc');
    fixture.detectChanges();
    expect(host.dynamicForm().fieldErrorMessage(buildFields()[0])).toBeNull();
  });

  it('un submitHandler exitoso pasa por "submitting" y vuelve a "idle", emitiendo `submitted`', async () => {
    const { fixture, host } = setup();
    host.form().patchValue({ nombre: 'Ana', email: 'ana@ejemplo.com' });

    const resultSubject = new Subject<unknown>();
    host.submitHandler.set({ submit: () => resultSubject.asObservable() });
    fixture.detectChanges();

    host.dynamicForm().onSubmit();
    fixture.detectChanges();
    expect(host.dynamicForm().status()).toBe('submitting');

    resultSubject.next({ id: 'nuevo-1' });
    resultSubject.complete();
    fixture.detectChanges();

    expect(host.dynamicForm().status()).toBe('idle');
    expect(host.submittedValues).toHaveLength(1);
  });

  it('un 400 ValidationProblemDetails real del submitHandler aplica el error al FormControl correcto Y muestra el mensaje general (BitcodeUiError), sin volver a mapearlo', () => {
    const { fixture, host } = setup();
    host.form().patchValue({ nombre: 'Ana', email: 'ana@ejemplo.com' });

    class FailingHandler implements BitcodeFormSubmitHandler<UsuarioForm> {
      submit(): Observable<unknown> {
        return throwError(() => buildValidationHttpError({ Nombre: ['El nombre ya está en uso.'] }));
      }
    }
    host.submitHandler.set(new FailingHandler());
    fixture.detectChanges();

    host.dynamicForm().onSubmit();
    fixture.detectChanges();

    const dynamicForm = host.dynamicForm();
    expect(dynamicForm.status()).toBe('error');
    expect(dynamicForm.submitError()?.kind).toBe('validation');
    expect(host.form().get('nombre')?.invalid).toBe(true);
    expect(dynamicForm.fieldErrorMessage(buildFields()[0])).toBe('El nombre ya está en uso.');
    // El campo no mencionado por el backend no se ve afectado.
    expect(host.form().get('email')?.valid).toBe(true);
    expect(host.submittedValues).toEqual([]);
  });

  it('un error no-validación (p. ej. 500) del submitHandler sólo muestra el mensaje general, sin tocar ningún control', () => {
    const { fixture, host } = setup();
    host.form().patchValue({ nombre: 'Ana', email: 'ana@ejemplo.com' });

    class FailingHandler implements BitcodeFormSubmitHandler<UsuarioForm> {
      submit(): Observable<unknown> {
        return throwError(() => new HttpErrorResponse({ status: 500 }));
      }
    }
    host.submitHandler.set(new FailingHandler());
    fixture.detectChanges();

    host.dynamicForm().onSubmit();
    fixture.detectChanges();

    const dynamicForm = host.dynamicForm();
    expect(dynamicForm.status()).toBe('error');
    expect(dynamicForm.submitError()?.kind).toBe('server');
    expect(host.form().get('nombre')?.valid).toBe(true);
  });

  it('un campo oculto por permiso (permissionEffect por defecto "hide") no se renderiza sin el permiso, y aparece al concederlo', () => {
    TestBed.configureTestingModule({
      providers: [{ provide: BitcodeSessionService, useClass: FakeSessionService }],
    });
    const fixture = TestBed.createComponent(HostComponent);
    const host = fixture.componentInstance;
    host.fields.set([
      ...buildFields(),
      { name: 'salario', label: 'Salario', requiredPermissions: 'rrhh.salarios.ver' },
    ]);
    host.form.set(
      new FormGroup({
        nombre: new FormControl(''),
        email: new FormControl(''),
        activo: new FormControl(false),
        salario: new FormControl(0),
      }),
    );
    const session = TestBed.inject(BitcodeSessionService) as unknown as FakeSessionService;
    fixture.detectChanges();

    expect(host.dynamicForm().visibleFields().map((f) => f.name)).not.toContain('salario');

    session.claims.set({ subject: 'user-1', roles: [], permissions: ['rrhh.salarios.ver'], raw: {} });
    fixture.detectChanges();
    expect(host.dynamicForm().visibleFields().map((f) => f.name)).toContain('salario');
  });

  it('un campo con permissionEffect "disable" permanece visible pero deshabilita el FormControl real sin el permiso', () => {
    TestBed.configureTestingModule({
      providers: [{ provide: BitcodeSessionService, useClass: FakeSessionService }],
    });
    const fixture = TestBed.createComponent(HostComponent);
    const host = fixture.componentInstance;
    host.fields.set([
      { name: 'estado', label: 'Estado', requiredPermissions: 'flujo.aprobar', permissionEffect: 'disable' },
    ]);
    host.form.set(new FormGroup({ estado: new FormControl('pendiente') }));
    const session = TestBed.inject(BitcodeSessionService) as unknown as FakeSessionService;
    fixture.detectChanges();

    expect(host.dynamicForm().visibleFields().map((f) => f.name)).toContain('estado');
    expect(host.form().get('estado')?.disabled).toBe(true);

    session.claims.set({ subject: 'user-1', roles: [], permissions: ['flujo.aprobar'], raw: {} });
    fixture.detectChanges();
    expect(host.form().get('estado')?.disabled).toBe(false);
  });

  it('un campo readOnly siempre deshabilita el FormControl, independientemente de permisos/sesión', () => {
    const { fixture, host } = setup();
    host.fields.set([{ name: 'nombre', label: 'Nombre', readOnly: true }]);
    host.form.set(new FormGroup({ nombre: new FormControl('valor fijo') }));
    fixture.detectChanges();

    expect(host.form().get('nombre')?.disabled).toBe(true);
  });
});
