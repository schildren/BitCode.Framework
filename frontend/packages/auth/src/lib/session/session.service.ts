import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { BITCODE_AUTH_CONFIG } from '../config/auth-config';
import { BitcodeSessionState, INITIAL_SESSION_STATE } from '../models/session-state.model';
import { mapToUserClaims } from './claims-mapper';

/**
 * Fuente única de verdad del lado cliente sobre "¿hay sesión activa?". Nunca guarda ni decodifica un
 * token: todo lo que mantiene es un estado en memoria (`signal`) derivado de la respuesta HTTP de
 * `sessionEndpoint`. La cookie HttpOnly de sesión del BFF (`bc-bff-session`, ver
 * `docs/guia-oidc-adapter.md`) es la única persistencia real, gestionada por el navegador -- este
 * servicio jamás la lee ni la escribe directamente.
 */
@Injectable({ providedIn: 'root' })
export class BitcodeSessionService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(BITCODE_AUTH_CONFIG);

  private readonly state = signal<BitcodeSessionState>(INITIAL_SESSION_STATE);
  private pendingCheck: Promise<BitcodeSessionState> | null = null;

  readonly status = computed(() => this.state().status);
  readonly claims = computed(() => this.state().claims);
  readonly isAuthenticated = computed(() => this.state().status === 'authenticated');

  /**
   * Resuelve la sesión actual contra el backend (`GET sessionEndpoint`, `withCredentials: true`).
   * Concurrente-seguro: si ya hay una resolución en curso, todos los llamadores comparten la misma
   * promesa en vez de disparar una petición HTTP por cada uno (p. ej. varios guards evaluando rutas en
   * paralelo al cargar la app).
   */
  checkSession(): Promise<BitcodeSessionState> {
    if (this.pendingCheck) {
      return this.pendingCheck;
    }

    this.state.set({ status: 'loading', claims: this.state().claims });

    this.pendingCheck = firstValueFrom(
      this.http.get<Record<string, unknown>>(this.config.sessionEndpoint, { withCredentials: true }),
    )
      .then((raw) => {
        const next: BitcodeSessionState = { status: 'authenticated', claims: mapToUserClaims(raw) };
        this.state.set(next);
        return next;
      })
      .catch(() => {
        // 401 (sin sesión) y cualquier otro fallo de red se tratan igual desde el punto de vista de la
        // UI: "no hay sesión utilizable ahora mismo". Un fallo transitorio de red no debe dejar a la app
        // pensando que el usuario está autenticado.
        const next: BitcodeSessionState = { status: 'anonymous', claims: null };
        this.state.set(next);
        return next;
      })
      .finally(() => {
        this.pendingCheck = null;
      });

    return this.pendingCheck;
  }

  /**
   * Invocado por `bitcodeAuthInterceptor` cuando una respuesta 401 de cualquier llamada protegida indica
   * que la sesión ya no es válida (expiró, fue revocada) -- refleja "sin sesión" de inmediato sin
   * esperar a un nuevo `checkSession()`.
   */
  markAnonymous(): void {
    this.state.set({ status: 'anonymous', claims: null });
  }

  /**
   * Limpia el estado en memoria del cliente (usado por logout). No hay ningún token que remover de
   * `localStorage`/`sessionStorage`: nunca se guardó uno ahí en primer lugar.
   */
  clear(): void {
    this.state.set({ status: 'anonymous', claims: null });
  }
}
