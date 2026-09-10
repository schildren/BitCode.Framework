import { BitcodeSessionService } from '@bitcode/auth';
import { Injectable, computed, inject } from '@angular/core';
import { BITCODE_MENU_ITEMS } from './menu.config';
import { filterMenuByPermissions } from './menu-filter';

/**
 * Resuelve el árbol de navegación VISIBLE para el actor de la sesión actual (F7-05): items configurados
 * (`BITCODE_MENU_ITEMS`) filtrados por permiso reutilizando `hasRequiredPermissions`/`@bitcode/auth`
 * (F7-04), sin reimplementar ningún chequeo de permisos propio.
 *
 * "Actualización controlada" (criterio de aceptación de F7-05) -- interpretación explícita, ya que el
 * Plan Maestro no lo detalla más: el menú debe recalcularse de forma predecible y reactiva cuando cambian
 * los claims del actor (login, logout, una sesión distinta tras `checkSession()`), sin recargar la
 * página, pero SIN parpadeos en emisiones intermedias del estado de sesión (p. ej. mientras `status` pasa
 * transitoriamente por `'loading'` mientras se reconfirma la MISMA sesión ya conocida).
 *
 * Se logra con `computed()` derivando pura y exclusivamente de `session.claims()` (no de `session.status()`
 * ni de ningún evento propio): `BitcodeSessionService.checkSession()` sólo cambia el `signal` de claims
 * cuando el valor REALMENTE cambia (una sesión distinta, o pasar a `null`) -- durante una resolución en
 * curso que reconfirma la sesión ya conocida, `claims()` conserva la misma referencia, así que este
 * `computed()` ni siquiera se re-evalúa mientras tanto (semántica estándar de Angular signals: un
 * `computed` sólo se marca dirty cuando una señal de la que depende cambia de valor, no cuando el
 * `signal` contenedor se vuelve a `set()` con contenido equivalente). Ver
 * `menu.service.spec.ts`, caso "no hay parpadeo a menú anónimo mientras se reconfirma la misma sesión".
 */
@Injectable({ providedIn: 'root' })
export class BitcodeMenuService {
  private readonly session = inject(BitcodeSessionService);
  private readonly items = inject(BITCODE_MENU_ITEMS);

  /** Árbol de navegación ya filtrado por permisos, listo para renderizar. */
  readonly menu = computed(() => filterMenuByPermissions(this.items, this.session.claims()));
}
