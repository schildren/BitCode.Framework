import { Component } from '@angular/core';
import { WorkflowTaskActions } from '@bitcode/workflow';

/** Página de demostración de F7-14 (ver `documentos-page.ts` para el criterio completo): `taskId` de
 * ejemplo -- las acciones fallarán contra un backend real inexistente en este demo, comportamiento
 * esperado (el objetivo es probar el code-splitting, no una pantalla de negocio funcional). */
@Component({
  selector: 'app-workflow-page',
  standalone: true,
  imports: [WorkflowTaskActions],
  template: `
    <h2>Workflow</h2>
    <p>Acciones sobre una tarea (demo de plataforma, F7-14).</p>
    <lib-workflow-task-actions taskId="demo-task-1" />
  `,
})
export default class WorkflowPage {}
