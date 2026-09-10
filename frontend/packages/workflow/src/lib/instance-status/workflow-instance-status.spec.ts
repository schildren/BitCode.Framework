// @vitest-environment jsdom
// @vitest-environment-options {"url": "http://127.0.0.1:58314/"}
import { provideHttpClient, withXhr } from '@angular/common/http';
import { Component, signal, viewChild } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { WorkflowTestServer } from '../testing/workflow-test-server';
import { WorkflowInstanceStatus } from './workflow-instance-status';

const PORT = 58314;

@Component({
  standalone: true,
  imports: [WorkflowInstanceStatus],
  template: `<lib-workflow-instance-status [instanceId]="instanceId()" />`,
})
class HostComponent {
  readonly statusComponent = viewChild.required(WorkflowInstanceStatus);
  readonly instanceId = signal('instance-1');
}

describe('WorkflowInstanceStatus (F7-09, contra un servidor HTTP real)', () => {
  let server: WorkflowTestServer;

  beforeAll(async () => {
    server = new WorkflowTestServer();
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

  async function waitLoaded(fixture: ComponentFixture<HostComponent>): Promise<void> {
    await new Promise((resolve) => setTimeout(resolve, 50));
    fixture.detectChanges();
  }

  it('carga instancia + versión + historial y resuelve el nombre del estado actual y las transiciones', async () => {
    const { fixture, host } = setup();
    await waitLoaded(fixture);

    const component = host.statusComponent();
    expect(component.status()).toBe('loaded');
    expect(component.estadoActualNombre()).toBe('Pendiente de revisión');
    expect(component.transicionesDisponibles().map((t) => t.accion)).toEqual(['Aprobar', 'Rechazar']);
    expect(component.historialOrdenado().map((entry) => entry.tipoEvento)).toEqual([
      'InstanciaIniciada',
      'TareaAsignada',
    ]);
  });

  it('una instancia inexistente (404) deja el componente en error con un BitcodeUiError', async () => {
    const { fixture, host } = setup();
    host.instanceId.set('instance-not-found');
    fixture.detectChanges();
    await waitLoaded(fixture);

    const component = host.statusComponent();
    expect(component.status()).toBe('error');
    expect(component.error()?.kind).toBe('not-found');
  });

  it('reload() vuelve a disparar la carga completa', async () => {
    const { fixture, host } = setup();
    await waitLoaded(fixture);

    const component = host.statusComponent();
    expect(component.status()).toBe('loaded');

    component.reload();
    fixture.detectChanges();
    expect(component.status()).toBe('loading');

    await waitLoaded(fixture);
    expect(component.status()).toBe('loaded');
  });
});
