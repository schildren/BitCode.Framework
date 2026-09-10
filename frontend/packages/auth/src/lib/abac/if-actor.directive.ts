import { Directive, effect, inject, input, TemplateRef, ViewContainerRef } from '@angular/core';
import { BitcodeUserClaims } from '../models/user-claims.model';
import { BitcodeSessionService } from '../session/session.service';

/** Predicado arbitrario sobre los claims del actor autenticado (`null` si no hay sesión resuelta). */
export type BitcodeActorPredicate = (claims: BitcodeUserClaims | null) => boolean;

/**
 * Directiva estructural GENÉRICA de ABAC-cliente (F7-04) -- ver la nota de alcance en
 * `abac/actor-attributes.ts` antes de usar esta directiva: NO reemplaza `AttributeScopeAbacRule` del
 * backend, porque no conoce el recurso concreto por sí sola.
 *
 * A diferencia de `*bitcodeHasPermission` (una regla fija: "¿tiene este permiso?"), esta directiva recibe
 * un PREDICADO cualquiera sobre los claims del actor, para que un componente que SÍ conoce el recurso
 * concreto pueda expresar su propia condición de alcance sin que este paquete tenga que anticiparla:
 *
 * ```html
 * <!-- el componente ya cargó `pedido()` (con su `sucursalId`) y compara contra el tenant del actor -->
 * <button *bitcodeIfActor="esDeMiSucursal">Aprobar pedido</button>
 * ```
 * ```ts
 * esDeMiSucursal = (claims: BitcodeUserClaims | null) => claims?.tenantId === this.pedido().sucursalId;
 * ```
 *
 * ADVERTENCIA EXPLÍCITA (idéntica a `*bitcodeHasPermission`): esto es UX, no seguridad. El predicado
 * corre contra claims potencialmente desactualizados del lado cliente; la regla de alcance real
 * (`AttributeScopeAbacRule`) se re-evalúa server-side en cada request contra el estado actual del recurso
 * y del actor, y es la única que puede efectivamente denegar la operación.
 */
@Directive({
  selector: '[bitcodeIfActor]',
  standalone: true,
})
export class BitcodeIfActorDirective {
  private readonly templateRef = inject(TemplateRef<unknown>);
  private readonly viewContainerRef = inject(ViewContainerRef);
  private readonly session = inject(BitcodeSessionService);

  readonly bitcodeIfActor = input.required<BitcodeActorPredicate>();

  private hasView = false;

  constructor() {
    effect(() => {
      const allowed = this.bitcodeIfActor()(this.session.claims());

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
