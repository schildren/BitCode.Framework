import { BITCODE_AUTH_CONFIG, BitcodeSessionService } from '@bitcode/auth';
import { provideBitcodeMenuItems } from '@bitcode/ui';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { TestBed } from '@angular/core/testing';
import { App } from './app';
import { SHELL_MENU_ITEMS } from './navigation/shell-menu.config';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideHttpClient(),
        provideRouter([]),
        provideBitcodeMenuItems(SHELL_MENU_ITEMS),
        // Sin `sessionEndpoint` real disponible en este arnés de test, apuntamos a un origen que
        // devuelve un rechazo de red inmediato -- `BitcodeSessionService`/`BitcodeMenuService` tratan
        // cualquier fallo de red igual que un 401 ("sin sesión utilizable ahora"), así que el shell
        // renderiza igualmente sin sesión resuelta (ver `navigation-shell.spec.ts` para el
        // comportamiento filtrado por permisos con una sesión real).
        { provide: BITCODE_AUTH_CONFIG, useValue: { sessionEndpoint: 'about:blank#unreachable' } },
      ],
    }).compileComponents();
  });

  it('renderiza la navegación principal (F7-05)', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('app-navigation-shell')).toBeTruthy();
    expect(compiled.querySelector('router-outlet')).toBeTruthy();
  });

  it('sin sesión resuelta, sólo muestra items de menú que no requieren permiso', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const linkTexts = Array.from(fixture.nativeElement.querySelectorAll('a')).map((a) =>
      (a as HTMLAnchorElement).textContent?.trim(),
    );
    expect(linkTexts).toContain('Inicio');
    expect(linkTexts).not.toContain('Reportes');

    // Confirma que no quedó una sesión autenticada "por accidente" (por ejemplo, si el `BITCODE_AUTH_CONFIG`
    // de arriba dejara de fallar como se espera).
    expect(TestBed.inject(BitcodeSessionService).isAuthenticated()).toBe(false);
  });
});
