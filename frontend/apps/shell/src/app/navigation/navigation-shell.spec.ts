import { BitcodeUserClaims } from '@bitcode/auth';
import { BitcodeMenuService, provideBitcodeMenuItems } from '@bitcode/ui';
import { provideRouter } from '@angular/router';
import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { BitcodeSessionService } from '@bitcode/auth';
import { SHELL_MENU_ITEMS } from './shell-menu.config';
import { NavigationShell } from './navigation-shell';

function claimsWith(permissions: readonly string[]): BitcodeUserClaims {
  return { subject: 'user-1', roles: [], permissions, raw: {} };
}

/**
 * Doble mínimo de `BitcodeSessionService` (mismo criterio que
 * `packages/auth/src/lib/permissions/has-permission.directive.spec.ts`): la resolución HTTP real de
 * sesión y la lógica de filtrado por permiso YA están cubiertas de punta a punta en
 * `packages/ui/src/lib/navigation/menu.service.spec.ts` contra un servidor HTTP real. Este spec es
 * deliberadamente sobre el COMPONENTE (renderizado, expandir/colapsar), no otra prueba de integración
 * HTTP redundante.
 */
class FakeSessionService {
  readonly claims = signal<BitcodeUserClaims | null>(null);
}

describe('NavigationShell (F7-05)', () => {
  function setup() {
    TestBed.configureTestingModule({
      imports: [NavigationShell],
      providers: [
        provideRouter([]),
        provideBitcodeMenuItems(SHELL_MENU_ITEMS),
        { provide: BitcodeSessionService, useClass: FakeSessionService },
      ],
    });
    const fixture = TestBed.createComponent(NavigationShell);
    const session = TestBed.inject(BitcodeSessionService) as unknown as FakeSessionService;
    return { fixture, session };
  }

  function topLevelLabels(fixture: ReturnType<typeof setup>['fixture']): string[] {
    return Array.from(
      fixture.nativeElement.querySelectorAll('.bc-nav > .bc-nav__list > .bc-nav__item > a.bc-nav__link, .bc-nav > .bc-nav__list > .bc-nav__item > button .bc-nav__label'),
    ).map((el) => (el as HTMLElement).textContent?.trim());
  }

  it('sin sesión, sólo se ve "Inicio" (ningún módulo de Fase 6 requiere permiso vacío)', () => {
    const { fixture } = setup();
    fixture.detectChanges();

    expect(topLevelLabels(fixture)).toEqual(['Inicio']);
  });

  it('con permiso de un único módulo, aparece el grupo que lo contiene pero colapsado por defecto', () => {
    const { fixture, session } = setup();
    session.claims.set(claimsWith(['identidad.usuarios.ver']));
    fixture.detectChanges();

    expect(topLevelLabels(fixture)).toEqual(['Inicio', 'Administración']);
    expect(fixture.nativeElement.querySelectorAll('.bc-nav__sublist')).toHaveLength(0);
  });

  it('expandir un grupo muestra sólo los hijos permitidos; colapsar lo vuelve a ocultar', () => {
    const { fixture, session } = setup();
    session.claims.set(claimsWith(['identidad.usuarios.ver']));
    fixture.detectChanges();

    const toggle = fixture.nativeElement.querySelector('.bc-nav__group-toggle') as HTMLButtonElement;
    toggle.click();
    fixture.detectChanges();

    const childLinks = Array.from(fixture.nativeElement.querySelectorAll('.bc-nav__sublist a')).map((a) =>
      (a as HTMLAnchorElement).textContent?.trim(),
    );
    expect(childLinks).toEqual(['Identidad y accesos']);

    toggle.click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('.bc-nav__sublist')).toHaveLength(0);
  });

  it('con permisos de todos los módulos, se ven los 5 grupos/items de nivel superior', () => {
    const { fixture, session } = setup();
    session.claims.set(
      claimsWith([
        'identidad.usuarios.ver',
        'organizacion.estructura.ver',
        'catalogos.items.ver',
        'features.flags.ver',
        'documentos.archivos.ver',
        'workflow.instancias.ver',
        'tareas.bandeja.ver',
        'notificaciones.mensajes.ver',
        'integraciones.conectores.ver',
        'importexport.trabajos.ver',
        'reporting.reportes.ver',
        'dashboard.tableros.ver',
      ]),
    );
    fixture.detectChanges();

    expect(topLevelLabels(fixture)).toEqual([
      'Inicio',
      'Administración',
      'Contenido y procesos',
      'Integraciones',
      'Analítica',
    ]);
  });

  it('el árbol filtrado proviene de BitcodeMenuService (@bitcode/ui), no de una copia propia del componente', () => {
    const { fixture } = setup();
    const menuService = TestBed.inject(BitcodeMenuService);
    fixture.detectChanges();

    expect((fixture.componentInstance as NavigationShell).menu()).toBe(menuService.menu());
  });
});
