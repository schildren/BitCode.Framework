import { Component } from '@angular/core';

/** Ruta raíz (`/inicio`), cargada eager como parte del bundle inicial -- a diferencia de
 * `documentos-page`/`workflow-page` (lazy), sirve de contraste para verificar en `docs/guia-frontend-
 * performance.md` que sólo las rutas marcadas `loadComponent` generan un chunk separado. */
@Component({
  selector: 'app-inicio-page',
  standalone: true,
  template: `<h2>Inicio</h2>`,
})
export default class InicioPage {}
