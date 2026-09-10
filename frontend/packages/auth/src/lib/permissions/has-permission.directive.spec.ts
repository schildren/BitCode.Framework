import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { BitcodeUserClaims } from '../models/user-claims.model';
import { BitcodeSessionService } from '../session/session.service';
import { BitcodeHasPermissionDirective } from './has-permission.directive';

function claimsWith(permissions: readonly string[]): BitcodeUserClaims {
  return { subject: 'user-1', roles: [], permissions, raw: {} };
}

/**
 * Doble mínimo de `BitcodeSessionService` -- únicamente el `signal` `claims`, que es todo lo que la
 * directiva consume. La resolución REAL de sesión (HTTP contra `/bff/session`) ya está probada de punta a
 * punta en `session/session.service.spec.ts` y `require-permission.guard.spec.ts` contra `BffTestDouble`;
 * este spec es deliberadamente un test de UNIDAD del comportamiento de la directiva (mostrar/ocultar
 * según el signal), no otra prueba de integración HTTP redundante.
 */
class FakeSessionService {
  readonly claims = signal<BitcodeUserClaims | null>(null);
}

@Component({
  standalone: true,
  imports: [BitcodeHasPermissionDirective],
  template: `
    <button *bitcodeHasPermission="'identidad.usuarios.crear'">Crear usuario</button>
    <button *bitcodeHasPermission="multiplesPermisos(); mode: modo()">Acción múltiple</button>
  `,
})
class HostComponent {
  readonly multiplesPermisos = signal<readonly string[]>([
    'identidad.usuarios.crear',
    'identidad.usuarios.roles.asignar',
  ]);
  readonly modo = signal<'all' | 'any'>('all');
}

describe('BitcodeHasPermissionDirective (*bitcodeHasPermission)', () => {
  function setup() {
    TestBed.configureTestingModule({
      providers: [{ provide: BitcodeSessionService, useClass: FakeSessionService }],
    });
    const fixture = TestBed.createComponent(HostComponent);
    const session = TestBed.inject(BitcodeSessionService) as unknown as FakeSessionService;
    return { fixture, session };
  }

  function buttonTexts(fixture: ReturnType<typeof setup>['fixture']): string[] {
    return Array.from(fixture.nativeElement.querySelectorAll('button')).map(
      (button) => (button as HTMLButtonElement).textContent?.trim() ?? '',
    );
  }

  it('sin sesión (claims null), no renderiza ningún elemento protegido por permiso', () => {
    const { fixture } = setup();
    fixture.detectChanges();

    expect(buttonTexts(fixture)).toHaveLength(0);
  });

  it('con el permiso concedido, renderiza el elemento único', () => {
    const { fixture, session } = setup();
    session.claims.set(claimsWith(['identidad.usuarios.crear']));
    fixture.detectChanges();

    expect(buttonTexts(fixture)).toContain('Crear usuario');
  });

  it('sin el permiso, el elemento permanece oculto (no está en el DOM, no sólo invisible por CSS)', () => {
    const { fixture, session } = setup();
    session.claims.set(claimsWith(['identidad.usuarios.ver']));
    fixture.detectChanges();

    expect(buttonTexts(fixture)).not.toContain('Crear usuario');
  });

  it('reacciona a cambios de sesión en vivo (login/logout) sin recrear el componente', () => {
    const { fixture, session } = setup();
    fixture.detectChanges();
    expect(buttonTexts(fixture)).toHaveLength(0);

    session.claims.set(claimsWith(['identidad.usuarios.crear']));
    fixture.detectChanges();
    expect(buttonTexts(fixture)).toContain('Crear usuario');

    session.claims.set(null);
    fixture.detectChanges();
    expect(buttonTexts(fixture)).not.toContain('Crear usuario');
  });

  it('mode "all" (default): exige TODOS los permisos de la lista', () => {
    const { fixture, session } = setup();
    session.claims.set(claimsWith(['identidad.usuarios.crear']));
    fixture.detectChanges();

    expect(buttonTexts(fixture)).not.toContain('Acción múltiple');
  });

  it('mode "any": alcanza con uno solo de los permisos de la lista', () => {
    const { fixture, session } = setup();
    session.claims.set(claimsWith(['identidad.usuarios.crear']));
    (fixture.componentInstance as HostComponent).modo.set('any');
    fixture.detectChanges();

    expect(buttonTexts(fixture)).toContain('Acción múltiple');
  });
});
