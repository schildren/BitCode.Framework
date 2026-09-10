import { provideHttpClient, withXhr } from '@angular/common/http';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { criticalA11yViolations, describeA11yViolations, runBitcodeA11yCheck } from '@bitcode/core/testing';
import { describe, expect, it } from 'vitest';
import { DEFAULT_WORKFLOW_TASK_ACTIONS, WorkflowTaskActionOption, WorkflowTaskActions } from './workflow-task-actions';

/** F7-12: no requiere un servidor HTTP real (a diferencia de `workflow-task-actions.spec.ts`) porque sólo
 * se verifica el DOM en estado inicial (`idle`), sin disparar ninguna llamada de red. */
@Component({
  standalone: true,
  imports: [WorkflowTaskActions],
  template: `<lib-workflow-task-actions [taskId]="taskId()" [actions]="actions()" />`,
})
class HostComponent {
  readonly taskId = signal('task-1');
  readonly actions = signal<readonly WorkflowTaskActionOption[]>(DEFAULT_WORKFLOW_TASK_ACTIONS);
}

describe('WorkflowTaskActions -- accesibilidad (F7-12)', () => {
  it('no tiene hallazgos críticos de axe-core en su estado inicial', async () => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(withXhr())] });
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();

    const results = await runBitcodeA11yCheck(fixture.nativeElement);
    const critical = criticalA11yViolations(results);

    expect(critical, describeA11yViolations(critical)).toEqual([]);
  });
});
