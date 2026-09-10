import { ChangeDetectionStrategy, Component, effect, inject, input, signal } from '@angular/core';
import { BitcodeErrorExperienceService, BitcodeHttpError, BitcodeUiError } from '@bitcode/core';
import { downloadFile } from '../download-file';
import { DocumentsService } from '../documents.service';
import { DocumentoVersionResponse, estadoEscaneoLabel, puedeDescargarse } from '../models/documento.model';

export type DocumentVersionListStatus = 'idle' | 'loading' | 'loaded' | 'error';

/**
 * Lista de versiones de UN documento con descarga segura por versión (F7-10) -- envuelve
 * `DocumentsService.listarVersiones`/`.descargar`. El botón "Descargar" sólo se habilita cuando
 * `puedeDescargarse(version.estadoEscaneo)` es verdadero (ver `models/documento.model.ts`) -- una versión
 * `PendienteEscaneo`/`Infectado` nunca se ofrece, aunque el backend igual la rechazaría con 409 si se
 * insistiera (defensa en profundidad en la UI, la autoridad real sigue siendo el backend).
 */
@Component({
  selector: 'lib-document-version-list',
  imports: [],
  templateUrl: './document-version-list.html',
  styleUrl: './document-version-list.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DocumentVersionList {
  private readonly service = inject(DocumentsService);
  private readonly errorExperience = inject(BitcodeErrorExperienceService);

  readonly documentId = input.required<string>();

  readonly status = signal<DocumentVersionListStatus>('idle');
  readonly versiones = signal<readonly DocumentoVersionResponse[]>([]);
  readonly error = signal<BitcodeUiError | null>(null);
  readonly descargandoNumero = signal<number | null>(null);

  readonly estadoEscaneoLabel = estadoEscaneoLabel;
  readonly puedeDescargarse = puedeDescargarse;

  constructor() {
    effect(() => {
      const id = this.documentId();
      this.load(id);
    });
  }

  reload(): void {
    this.load(this.documentId());
  }

  descargar(version: DocumentoVersionResponse): void {
    if (this.descargandoNumero() !== null) {
      return;
    }
    this.descargandoNumero.set(version.numero);
    this.service.descargar(this.documentId(), version.numero).subscribe({
      next: ({ blob, nombreArchivo }) => {
        downloadFile(blob, nombreArchivo);
        this.descargandoNumero.set(null);
      },
      error: (rawError: unknown) => {
        this.descargandoNumero.set(null);
        this.error.set(this.toUiError(rawError));
      },
    });
  }

  private load(documentId: string): void {
    this.status.set('loading');
    this.error.set(null);

    this.service.listarVersiones(documentId).subscribe({
      next: (versiones) => {
        this.versiones.set(versiones);
        this.status.set('loaded');
      },
      error: (rawError: unknown) => {
        this.status.set('error');
        this.error.set(this.toUiError(rawError));
      },
    });
  }

  private toUiError(error: unknown): BitcodeUiError {
    if (error instanceof BitcodeHttpError) {
      return error.uiError;
    }
    return this.errorExperience.fromHttpError(error);
  }
}
