// @vitest-environment jsdom
// @vitest-environment-options {"url": "http://127.0.0.1:58321/"}
import { provideHttpClient, withXhr } from '@angular/common/http';
import { Component, signal, viewChild } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { DocumentsTestServer } from '../testing/documents-test-server';
import { DocumentUpload, DocumentUploadMode } from './document-upload';

const PORT = 58321;

@Component({
  standalone: true,
  imports: [DocumentUpload],
  template: `
    <lib-document-upload
      [mode]="mode()"
      [documentId]="documentId()"
      [titulo]="'Contrato'"
      [clasificacion]="'Contrato'"
      [retencionDias]="365"
      (uploaded)="onUploaded($event)"
    />
  `,
})
class HostComponent {
  readonly upload = viewChild.required(DocumentUpload);
  readonly mode = signal<DocumentUploadMode>('crear');
  readonly documentId = signal<string | undefined>(undefined);
  uploadedIds: string[] = [];

  onUploaded(id: string): void {
    this.uploadedIds.push(id);
  }
}

function selectFile(fixture: ComponentFixture<HostComponent>, file: File): void {
  const input = fixture.nativeElement.querySelector('input[type="file"]') as HTMLInputElement;
  Object.defineProperty(input, 'files', { value: [file], configurable: true });
  input.dispatchEvent(new Event('change'));
  fixture.detectChanges();
}

describe('DocumentUpload (F7-10, contra un servidor HTTP real, progreso real)', () => {
  let server: DocumentsTestServer;

  beforeAll(async () => {
    server = new DocumentsTestServer();
    await server.start(PORT);
  });

  afterAll(async () => {
    await server.stop();
  });

  function setup(): { fixture: ComponentFixture<HostComponent>; host: HostComponent } {
    TestBed.configureTestingModule({ providers: [provideHttpClient(withXhr())] });
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    return { fixture, host: fixture.componentInstance };
  }

  it('sube un archivo (mode="crear") y emite `uploaded` con el id del documento', async () => {
    const { fixture, host } = setup();
    selectFile(fixture, new File(['contenido'], 'contrato.pdf', { type: 'application/pdf' }));

    host.upload().upload();
    fixture.detectChanges();
    expect(host.upload().status()).toBe('uploading');

    await new Promise((resolve) => setTimeout(resolve, 80));
    fixture.detectChanges();

    expect(host.upload().status()).toBe('idle');
    expect(host.upload().percent()).toBe(100);
    expect(host.uploadedIds).toEqual(['documento-nuevo-1']);
  });

  it('sube una nueva versión (mode="version") y emite `uploaded` con el id de la versión', async () => {
    const { fixture, host } = setup();
    host.mode.set('version');
    host.documentId.set('doc-1');
    fixture.detectChanges();
    selectFile(fixture, new File(['contenido'], 'contrato-v2.pdf', { type: 'application/pdf' }));

    host.upload().upload();
    await new Promise((resolve) => setTimeout(resolve, 80));
    fixture.detectChanges();

    expect(host.uploadedIds).toEqual(['version-nueva-1']);
  });

  it('no permite subir sin haber seleccionado un archivo', () => {
    const { host } = setup();
    host.upload().upload();
    expect(host.upload().status()).toBe('idle');
    expect(host.uploadedIds).toEqual([]);
  });
});
