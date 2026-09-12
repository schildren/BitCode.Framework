import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import { BitcodeErrorExperienceService, BitcodeHttpError, BitcodeUiError } from '@bitcode/core';
import { DocumentsService } from '../documents.service';

export type DocumentUploadMode = 'crear' | 'version';
export type DocumentUploadStatus = 'idle' | 'uploading' | 'error';

/**
 * Carga de UN archivo (F7-10) -- envuelve `DocumentsService.crear`/`.subirVersion`, mostrando el progreso
 * REAL reportado por `HttpClient` (`reportProgress: true`), nunca una barra de progreso simulada con un
 * timer. Dos modos, mismo componente (mismo criterio que reutilizar `BitcodeDynamicForm` para altas y
 * ediciones en `@bitcode/forms`, F7-08):
 *
 * - `mode="crear"`: alta inicial (`titulo`/`clasificacion`/`retencionDias` requeridos como inputs propios
 *   de este componente -- no reutiliza `BitcodeDynamicForm` porque el envío es multipart, no JSON, y
 *   `BitcodeFormSubmitHandler<T>.submit` no modela progreso, sólo éxito/error).
 * - `mode="version"`: nueva versión sobre `documentId` (input requerido en este modo), sólo el archivo.
 *
 * Igual criterio de error que el resto de F7-0x: nunca un mensaje inventado, siempre `BitcodeUiError` vía
 * `BitcodeErrorExperienceService`/`BitcodeHttpError.uiError`.
 */
@Component({
  selector: 'lib-document-upload',
  imports: [],
  templateUrl: './document-upload.html',
  styleUrl: './document-upload.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DocumentUpload {
  private readonly service = inject(DocumentsService);
  private readonly errorExperience = inject(BitcodeErrorExperienceService);

  readonly mode = input<DocumentUploadMode>('crear');
  readonly documentId = input<string | undefined>(undefined);
  readonly titulo = input<string>('');
  readonly descripcion = input<string | undefined>(undefined);
  readonly clasificacion = input<string>('');
  readonly retencionDias = input<number>(0);

  /** Emite el id devuelto por el backend: el `Guid` del documento (`mode="crear"`) o de la nueva versión
   * (`mode="version"`) -- ver `DocumentUploadProgress`. */
  readonly uploaded = output<string>();

  readonly status = signal<DocumentUploadStatus>('idle');
  readonly percent = signal(0);
  readonly error = signal<BitcodeUiError | null>(null);
  readonly selectedFileName = signal<string | null>(null);

  private selectedFile: File | null = null;

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    this.selectedFile = file;
    this.selectedFileName.set(file?.name ?? null);
  }

  upload(): void {
    if (!this.selectedFile || this.status() === 'uploading') {
      return;
    }

    this.status.set('uploading');
    this.percent.set(0);
    this.error.set(null);

    const upload$ =
      this.mode() === 'crear'
        ? this.service.crear({
            titulo: this.titulo(),
            descripcion: this.descripcion(),
            clasificacion: this.clasificacion(),
            retencionDias: this.retencionDias(),
            archivo: this.selectedFile,
          })
        : this.service.subirVersion(this.requireDocumentId(), this.selectedFile);

    upload$.subscribe({
      next: (progress) => {
        if (progress.status === 'uploading') {
          this.percent.set(progress.percent);
          return;
        }
        this.status.set('idle');
        this.percent.set(100);
        this.uploaded.emit(progress.id);
      },
      error: (rawError: unknown) => {
        this.status.set('error');
        this.error.set(this.toUiError(rawError));
      },
    });
  }

  private requireDocumentId(): string {
    const id = this.documentId();
    if (!id) {
      throw new Error('DocumentUpload: "documentId" es requerido cuando mode="version".');
    }
    return id;
  }

  private toUiError(error: unknown): BitcodeUiError {
    if (error instanceof BitcodeHttpError) {
      return error.uiError;
    }
    return this.errorExperience.fromHttpError(error);
  }
}
