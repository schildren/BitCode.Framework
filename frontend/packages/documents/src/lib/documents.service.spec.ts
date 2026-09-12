// @vitest-environment jsdom
// @vitest-environment-options {"url": "http://127.0.0.1:58320/"}
import { HttpErrorResponse, provideHttpClient, withXhr } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom, lastValueFrom, toArray } from 'rxjs';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { DocumentsTestServer } from './testing/documents-test-server';
import { DocumentsService } from './documents.service';

const PORT = 58320;

describe('DocumentsService (contra un servidor HTTP real, mismo contrato que Documents)', () => {
  let server: DocumentsTestServer;

  beforeAll(async () => {
    server = new DocumentsTestServer();
    await server.start(PORT);
  });

  afterAll(async () => {
    await server.stop();
  });

  function setup(): DocumentsService {
    TestBed.configureTestingModule({ providers: [provideHttpClient(withXhr())] });
    return TestBed.inject(DocumentsService);
  }

  it('crear() reporta progreso real y termina con { status: "done", id }', async () => {
    const service = setup();
    const archivo = new File(['contenido'.repeat(1000)], 'contrato.pdf', { type: 'application/pdf' });

    const events = await lastValueFrom(
      service
        .crear({ titulo: 'Contrato', clasificacion: 'Contrato', retencionDias: 365, archivo })
        .pipe(toArray()),
    );

    expect(events.length).toBeGreaterThan(0);
    const last = events[events.length - 1];
    expect(last).toEqual({ status: 'done', id: 'documento-nuevo-1' });
    expect(events.some((event) => event.status === 'uploading')).toBe(true);
  });

  it('subirVersion() termina con el id de la nueva versión', async () => {
    const service = setup();
    const archivo = new File(['contenido'], 'contrato-v2.pdf', { type: 'application/pdf' });

    const events = await lastValueFrom(service.subirVersion('doc-1', archivo).pipe(toArray()));
    expect(events[events.length - 1]).toEqual({ status: 'done', id: 'version-nueva-1' });
  });

  it('obtener() devuelve la metadata real del documento', async () => {
    const service = setup();
    const documento = await firstValueFrom(service.obtener('doc-1'));
    expect(documento.titulo).toBe('Contrato de servicio');
    expect(documento.versionActualNumero).toBe(1);
  });

  it('obtener() propaga 404 para un documento inexistente', async () => {
    const service = setup();
    await expect(firstValueFrom(service.obtener('doc-not-found'))).rejects.toMatchObject({ status: 404 });
  });

  it('listar() pagina server-side', async () => {
    const service = setup();
    const result = await firstValueFrom(service.listar(1, 10));
    expect(result.items).toHaveLength(10);
    expect(result.totalCount).toBe(45);
  });

  it('listarVersiones() devuelve todas las versiones con su estado de escaneo', async () => {
    const service = setup();
    const versiones = await firstValueFrom(service.listarVersiones('doc-1'));
    expect(versiones).toHaveLength(2);
    expect(versiones[0].estadoEscaneo).toBe(1);
  });

  it('descargar() devuelve el blob y el nombre de archivo real de Content-Disposition', async () => {
    const service = setup();
    const { blob, nombreArchivo } = await firstValueFrom(service.descargar('doc-1', 1));
    expect(nombreArchivo).toBe('contrato-v1.pdf');
    expect(blob.size).toBeGreaterThan(0);
  });

  it('descargar() propaga 409 para una versión no descargable (escaneo pendiente/infectado)', async () => {
    const service = setup();
    try {
      await firstValueFrom(service.descargar('doc-infectado'));
      expect.unreachable('debía rechazar con 409');
    } catch (error) {
      expect(error).toBeInstanceOf(HttpErrorResponse);
      expect((error as HttpErrorResponse).status).toBe(409);
    }
  });

  it('disponer() resuelve 204 sin body', async () => {
    const service = setup();
    await expect(firstValueFrom(service.disponer('doc-1'))).resolves.toBeNull();
  });

  it('disponer() propaga 409 cuando la retención sigue vigente (doc-conflict)', async () => {
    const service = setup();
    await expect(firstValueFrom(service.disponer('doc-conflict'))).rejects.toMatchObject({ status: 409 });
  });
});
