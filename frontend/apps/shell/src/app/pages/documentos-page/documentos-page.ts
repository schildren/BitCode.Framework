import { Component } from '@angular/core';
import { DocumentUpload } from '@bitcode/documents';

/**
 * Página de demostración de F7-14 ("Lazy loading, budgets y análisis de bundle"): wiring mínimo de
 * `DocumentUpload` (`@bitcode/documents`, F7-10) cargado como ruta perezosa (`loadComponent`) -- prueba que
 * el code-splitting real funciona (chunk separado en el build de producción, ver
 * `docs/guia-frontend-performance.md`), no sólo una promesa de que "se podría" hacer lazy loading.
 *
 * Deliberadamente sin wiring a un backend real (`documentId`/persistencia) -- esta página es un demo de
 * plataforma, no una pantalla de negocio terminada; eso es responsabilidad de una app consumidora real
 * (Fase 8).
 */
@Component({
  selector: 'app-documentos-page',
  standalone: true,
  imports: [DocumentUpload],
  template: `
    <h2>Documentos</h2>
    <p>Carga de documento (demo de plataforma, F7-14).</p>
    <lib-document-upload titulo="Documento de ejemplo" clasificacion="General" [retencionDias]="365" />
  `,
})
export default class DocumentosPage {}
