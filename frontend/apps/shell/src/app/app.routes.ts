import { Route } from '@angular/router';

/**
 * F7-14 ("Lazy loading, budgets y análisis de bundle"): las rutas de `procesos/*` -- correspondientes a los
 * `link` ya declarados en `shell-menu.config.ts` (F7-05) -- se cargan perezosamente con `loadComponent`,
 * generando un chunk JS separado por ruta en el build de producción (verificado, ver
 * `docs/guia-frontend-performance.md`), en vez de sumarse al bundle inicial. `/inicio` es la única ruta
 * eager (importada directamente), para poder comparar contra las lazy en el análisis de bundle.
 *
 * Alcance deliberadamente mínimo (demo de plataforma, no pantallas de negocio terminadas) -- ver el
 * comentario de cada página en `pages/*`.
 */
export const appRoutes: Route[] = [
  {
    path: 'inicio',
    loadComponent: () => import('./pages/inicio-page/inicio-page'),
  },
  {
    path: 'procesos/documentos',
    loadComponent: () => import('./pages/documentos-page/documentos-page'),
  },
  {
    path: 'procesos/workflow',
    loadComponent: () => import('./pages/workflow-page/workflow-page'),
  },
  { path: '', redirectTo: 'inicio', pathMatch: 'full' },
];
