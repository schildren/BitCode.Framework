import { Directive, effect, inject, input, TemplateRef, ViewContainerRef } from '@angular/core';
import { BitcodeSessionService } from '../session/session.service';
import { BitcodePermissionMode, hasRequiredPermissions } from './permission-checks';

/**
 * Directiva estructural (F7-04) que muestra/oculta el contenido asociado según el/los permiso(s) del
 * usuario autenticado -- pensada para ocultar acciones que el actor no puede ejecutar (p. ej. un botón
 * "Crear usuario"), sin que cada componente reimplemente `session.claims()?.permissions.includes(...)` a
 * mano.
 *
 * Uso:
 * ```html
 * <button *bitcodeHasPermission="'identidad.usuarios.crear'" (click)="crear()">Crear usuario</button>
 *
 * <!-- múltiples permisos, con la semántica explícita (ver permission-checks.ts) -->
 * <button *bitcodeHasPermission="['identidad.usuarios.crear', 'identidad.usuarios.roles.asignar']; mode: 'any'">
 *   ...
 * </button>
 * ```
 *
 * ADVERTENCIA EXPLÍCITA (criterio de aceptación de F7-04, "la UI no sustituye validación backend"): esta
 * directiva SÓLO decide si el DOM renderiza o no un elemento -- es UX, no seguridad. Ocultar un botón no
 * impide que alguien invoque la operación subyacente por otra vía (llamando al servicio HTTP
 * directamente desde la consola del navegador, reproduciendo la request con otra herramienta, etc.). La
 * única protección real sigue siendo `[RequirePermission]`/ABAC del lado servidor -- ver
 * `has-permission.directive.spec.ts`, caso "ocultar el botón no impide el 403 real si el permiso falta,
 * y tampoco lo evita si el permiso está mal cacheado del lado cliente".
 */
@Directive({
  selector: '[bitcodeHasPermission]',
  standalone: true,
})
export class BitcodeHasPermissionDirective {
  private readonly templateRef = inject(TemplateRef<unknown>);
  private readonly viewContainerRef = inject(ViewContainerRef);
  private readonly session = inject(BitcodeSessionService);

  readonly bitcodeHasPermission = input.required<string | readonly string[]>();
  readonly bitcodeHasPermissionMode = input<BitcodePermissionMode>('all');

  private hasView = false;

  constructor() {
    effect(() => {
      const claims = this.session.claims();
      const allowed = hasRequiredPermissions(claims, this.bitcodeHasPermission(), this.bitcodeHasPermissionMode());

      if (allowed && !this.hasView) {
        this.viewContainerRef.createEmbeddedView(this.templateRef);
        this.hasView = true;
      } else if (!allowed && this.hasView) {
        this.viewContainerRef.clear();
        this.hasView = false;
      }
    });
  }
}
