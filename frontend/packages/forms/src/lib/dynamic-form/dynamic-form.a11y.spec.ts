import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormControl, FormGroup, Validators } from '@angular/forms';
import { criticalA11yViolations, describeA11yViolations, runBitcodeA11yCheck } from '@bitcode/core';
import { describe, expect, it } from 'vitest';
import { BitcodeFormFieldConfig } from '../models/form-field.model';
import { BitcodeDynamicForm } from './dynamic-form';

/** F7-12: "Accesibilidad... Cero hallazgos críticos", verificado con axe-core (ver limitaciones jsdom en
 * `@bitcode/core`/`a11y-harness.ts`) contra el DOM real que renderiza `BitcodeDynamicForm`, no contra un
 * fragmento HTML aislado -- así cualquier regresión real (p. ej. un campo nuevo sin label asociado) la
 * detecta este test, no sólo una revisión manual. */

function buildFields(): BitcodeFormFieldConfig[] {
  return [
    { name: 'nombre', label: 'Nombre', validators: { required: true }, hint: 'Nombre completo' },
    { name: 'categoria', label: 'Categoría', type: 'select', options: [{ label: 'A', value: 'a' }] },
    { name: 'descripcion', label: 'Descripción', type: 'textarea' },
    { name: 'activo', label: 'Activo', type: 'checkbox' },
  ];
}

@Component({
  standalone: true,
  imports: [BitcodeDynamicForm],
  template: `<lib-bitcode-dynamic-form [fields]="fields()" [form]="form()" />`,
})
class HostComponent {
  readonly fields = signal<readonly BitcodeFormFieldConfig[]>(buildFields());
  readonly form = signal<FormGroup>(
    new FormGroup({
      nombre: new FormControl('', Validators.required),
      categoria: new FormControl('a'),
      descripcion: new FormControl(''),
      activo: new FormControl(false),
    }),
  );
}

describe('BitcodeDynamicForm -- accesibilidad (F7-12)', () => {
  it('no tiene hallazgos críticos de axe-core en su estado inicial', async () => {
    TestBed.configureTestingModule({});
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();

    const results = await runBitcodeA11yCheck(fixture.nativeElement);
    const critical = criticalA11yViolations(results);

    expect(critical, describeA11yViolations(critical)).toEqual([]);
  });

  it('no tiene hallazgos críticos con un campo requerido inválido mostrando su error (role="alert")', async () => {
    TestBed.configureTestingModule({});
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    fixture.componentInstance.form().get('nombre')?.markAsTouched();
    fixture.componentInstance.form().get('nombre')?.updateValueAndValidity();
    fixture.detectChanges();

    const results = await runBitcodeA11yCheck(fixture.nativeElement);
    const critical = criticalA11yViolations(results);

    expect(critical, describeA11yViolations(critical)).toEqual([]);
  });
});
