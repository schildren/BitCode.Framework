// @vitest-environment jsdom
// @vitest-environment-options {"url": "http://127.0.0.1:58313/"}
import { provideHttpClient, withXhr } from '@angular/common/http';
import { Component, signal, viewChild } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { WorkflowTestServer } from '../testing/workflow-test-server';
import { WorkflowTaskActions, WorkflowTaskActionOption, DEFAULT_WORKFLOW_TASK_ACTIONS } from './workflow-task-actions';

const PORT = 58313;

@Component({
  standalone: true,
  imports: [WorkflowTaskActions],
  template: `
    <lib-workflow-task-actions
      [taskId]="taskId()"
      [actions]="actions()"
      (resolved)="onResolved()"
      (delegated)="onDelegated()"
    />
  `,
})
class HostComponent {
  readonly taskAction = viewChild.required(WorkflowTaskActions);
  readonly taskId = signal('task-1');
  readonly actions = signal<readonly WorkflowTaskActionOption[]>(DEFAULT_WORKFLOW_TASK_ACTIONS);
  resolvedCount = 0;
  delegatedCount = 0;

  onResolved(): void {
    this.resolvedCount++;
  }

  onDelegated(): void {
    this.delegatedCount++;
  }
}

describe('WorkflowTaskActions (F7-09, contra un servidor HTTP real)', () => {
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

  it('resolver con éxito emite `resolved` y vuelve a idle', async () => {
    const { fixture, host } = setup();

    host.taskAction().resolverAccion('Aprobar');
    fixture.detectChanges();
    expect(host.taskAction().status()).toBe('resolving');

    await new Promise((resolve) => setTimeout(resolve, 50));
    fixture.detectChanges();

    expect(host.taskAction().status()).toBe('idle');
    expect(host.resolvedCount).toBe(1);
    expect(host.taskAction().error()).toBeNull();
  });

  it('un 403 (task-forbidden) deja el componente en error con un BitcodeUiError, sin emitir `resolved`', async () => {
    const { fixture, host } = setup();
    host.taskId.set('task-forbidden');
    fixture.detectChanges();

    host.taskAction().resolverAccion('Aprobar');
    await new Promise((resolve) => setTimeout(resolve, 50));
    fixture.detectChanges();

    expect(host.taskAction().status()).toBe('error');
    expect(host.taskAction().error()?.kind).toBe('forbidden');
    expect(host.resolvedCount).toBe(0);
  });

  it('un 409 (task-conflict) deja el componente en error con kind "conflict"', async () => {
    const { fixture, host } = setup();
    host.taskId.set('task-conflict');
    fixture.detectChanges();

    host.taskAction().resolverAccion('Aprobar');
    await new Promise((resolve) => setTimeout(resolve, 50));
    fixture.detectChanges();

    expect(host.taskAction().status()).toBe('error');
    expect(host.taskAction().error()?.kind).toBe('conflict');
  });

  it('ignora un segundo click mientras ya está resolviendo (previene doble envío)', async () => {
    const { fixture, host } = setup();

    host.taskAction().resolverAccion('Aprobar');
    fixture.detectChanges();
    expect(host.taskAction().status()).toBe('resolving');

    host.taskAction().resolverAccion('Rechazar');

    await new Promise((resolve) => setTimeout(resolve, 50));
    fixture.detectChanges();

    expect(host.resolvedCount).toBe(1);
  });
});
