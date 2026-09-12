import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { BitcodeUserClaims } from '../models/user-claims.model';
import { BitcodeSessionService } from '../session/session.service';
import { BitcodeActorPredicate, BitcodeIfActorDirective } from './if-actor.directive';

function claimsWith(tenantId: string): BitcodeUserClaims {
  return { subject: 'user-1', roles: [], permissions: [], tenantId, raw: {} };
}

/** Ver el comentario equivalente en `permissions/has-permission.directive.spec.ts`. */
class FakeSessionService {
  readonly claims = signal<BitcodeUserClaims | null>(null);
}

@Component({
  standalone: true,
  imports: [BitcodeIfActorDirective],
  template: `<button *bitcodeIfActor="predicado()">Aprobar pedido de la sucursal Norte</button>`,
})
class HostComponent {
  // Ejemplo de ABAC "conocido por el componente": este predicado sabe que el recurso concreto (un
  // pedido ya cargado) pertenece a la sucursal "norte" -- información que ninguna directiva/guard
  // genérico de este paquete podría tener de antemano (ver abac/actor-attributes.ts).
  readonly predicado = signal<BitcodeActorPredicate>((claims) => claims?.tenantId === 'norte');
}

describe('BitcodeIfActorDirective (*bitcodeIfActor) -- mecanismo ABAC genérico basado en predicado', () => {
  function setup() {
    TestBed.configureTestingModule({
      providers: [{ provide: BitcodeSessionService, useClass: FakeSessionService }],
    });
    const fixture = TestBed.createComponent(HostComponent);
    const session = TestBed.inject(BitcodeSessionService) as unknown as FakeSessionService;
    return { fixture, session };
  }

  it('sin sesión, el predicado recibe null y el elemento no se renderiza', () => {
    const { fixture } = setup();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('button')).toBeNull();
  });

  it('con un actor cuyo atributo satisface el predicado, renderiza', () => {
    const { fixture, session } = setup();
    session.claims.set(claimsWith('norte'));
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('button')).not.toBeNull();
  });

  it('con un actor cuyo atributo NO satisface el predicado (otra sucursal/tenant), no renderiza', () => {
    const { fixture, session } = setup();
    session.claims.set(claimsWith('sur'));
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('button')).toBeNull();
  });

  it('un predicado arbitrario distinto (no ligado a tenantId) también funciona -- es genérico, no una regla fija', () => {
    TestBed.configureTestingModule({
      providers: [{ provide: BitcodeSessionService, useClass: FakeSessionService }],
    });
    const fixture = TestBed.createComponent(HostComponent);
    const session = TestBed.inject(BitcodeSessionService) as unknown as FakeSessionService;
    (fixture.componentInstance as HostComponent).predicado.set(
      (claims) => (claims?.raw['nivelAprobacionMinimo'] as number | undefined) !== undefined,
    );
    session.claims.set({ subject: 'user-2', roles: [], permissions: [], raw: { nivelAprobacionMinimo: 3 } });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('button')).not.toBeNull();
  });
});
