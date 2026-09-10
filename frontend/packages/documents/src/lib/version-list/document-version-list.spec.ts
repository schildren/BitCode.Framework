// @vitest-environment jsdom
// @vitest-environment-options {"url": "http://127.0.0.1:58322/"}
import { provideHttpClient, withXhr } from '@angular/common/http';
import { Component, signal, viewChild } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { afterAll, beforeAll, describe, expect, it, vi } from 'vitest';
import { DocumentsTestServer } from '../testing/documents-test-server';
import { DocumentVersionList } from './document-version-list';

const PORT = 58322;

@Component({
  standalone: true,
  imports: [DocumentVersionList],
  template: `<lib-document-version-list [documentId]="documentId()" />`,
})
class HostComponent {
  readonly versionList = viewChild.required(DocumentVersionList);
  readonly documentId = signal('doc-1');
}

describe('DocumentVersionList (F7-10, contra un servidor HTTP real)', () => {
  let server: DocumentsTestServer;

  beforeAll(async () => {
    server = new DocumentsTestServer();
    await server.start(PORT);
    URL.createObjectURL = vi.fn(() => 'blob:mock-url');
    URL.revokeObjectURL = vi.fn();
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
  });

  afterAll(async () => {
    await server.stop();
    vi.restoreAllMocks();
  });

  function setup(): { fixture: ComponentFixture<HostComponent>; host: HostComponent } {
    TestBed.configureTestingModule({ providers: [provideHttpClient(withXhr())] });
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    return { fixture, host: fixture.componentInstance };
  }

  async function waitLoaded(fixture: ComponentFixture<HostComponent>): Promise<void> {
    await new Promise((resolve) => setTimeout(resolve, 50));
    fixture.detectChanges();
  }

  it('carga las versiones reales del documento', async () => {
    const { fixture, host } = setup();
    await waitLoaded(fixture);

    const component = host.versionList();
    expect(component.status()).toBe('loaded');
    expect(component.versiones()).toHaveLength(2);
    expect(component.versiones()[0].nombreArchivo).toBe('contrato.pdf');
  });

  it('puedeDescargarse es falso para una versión con escaneo pendiente', async () => {
    const { fixture, host } = setup();
    await waitLoaded(fixture);

    const component = host.versionList();
    const pendiente = component.versiones().find((v) => v.numero === 2)!;
    expect(component.puedeDescargarse(pendiente.estadoEscaneo)).toBe(false);
  });

  it('descargar() trae el blob real y dispara el <a> de descarga', async () => {
    const { fixture, host } = setup();
    await waitLoaded(fixture);

    const component = host.versionList();
    const limpia = component.versiones().find((v) => v.numero === 1)!;
    component.descargar(limpia);

    await new Promise((resolve) => setTimeout(resolve, 50));
    fixture.detectChanges();

    expect(component.descargandoNumero()).toBeNull();
    expect(HTMLAnchorElement.prototype.click).toHaveBeenCalled();
  });

  it('un error de red real (servidor detenido) deja el componente en error, y reload() reintenta', async () => {
    const { fixture, host } = setup();
    await waitLoaded(fixture);
    expect(host.versionList().status()).toBe('loaded');

    await server.stop();
    host.versionList().reload();
    await waitLoaded(fixture);
    expect(host.versionList().status()).toBe('error');
    expect(host.versionList().error()?.kind).toBe('network');

    // Se reinicia el servidor para no afectar el `afterAll` (que también llama `stop()`).
    await server.start(PORT);
    host.versionList().reload();
    await waitLoaded(fixture);
    expect(host.versionList().status()).toBe('loaded');
  });
});
