import { provideHttpClient, withXhr } from '@angular/common/http';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { criticalA11yViolations, describeA11yViolations, runBitcodeA11yCheck } from '@bitcode/core/testing';
import { describe, expect, it } from 'vitest';
import { DocumentUpload, DocumentUploadMode } from './document-upload';

/** F7-12: estado inicial (sin archivo seleccionado, sin llamada de red). Cubre en particular la
 * corrección de F7-12 al `<input type="file">` sin label asociado -- sin el `<label for>` agregado, axe-core
 * reporta la regla `label` como hallazgo crítico acá. */
@Component({
  standalone: true,
  imports: [DocumentUpload],
  template: `<lib-document-upload [mode]="mode()" [titulo]="'Contrato'" [clasificacion]="'Contrato'" [retencionDias]="365" />`,
})
class HostComponent {
  readonly mode = signal<DocumentUploadMode>('crear');
}

describe('DocumentUpload -- accesibilidad (F7-12)', () => {
  it('no tiene hallazgos críticos de axe-core en su estado inicial', async () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(withXhr())] });
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();

    const results = await runBitcodeA11yCheck(fixture.nativeElement);
    const critical = criticalA11yViolations(results);

    expect(critical, describeA11yViolations(critical)).toEqual([]);
  });
});
