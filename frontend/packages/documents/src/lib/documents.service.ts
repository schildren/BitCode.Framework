import { HttpClient, HttpEvent, HttpEventType, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { DocumentoResponse, DocumentoVersionResponse } from './models/documento.model';

/** Forma REAL de `PagedResult<T>` (`src/Shared.Kernel/PagedResult.cs`), mismo criterio que
 * `TaskInboxPagedResultDto` de `@bitcode/workflow`. */
interface DocumentosPagedResultDto {
  readonly items: readonly DocumentoResponse[];
  readonly page: number;
  readonly pageSize: number;
  readonly totalCount: number;
}

/** Input para crear un documento -- mismo campo `[FromForm]` que
 * `DocumentsEndpointRouteBuilderExtensions.MapDocumentsEndpoints` (`POST /api/v1/documentos`). */
export interface CrearDocumentoInput {
  readonly titulo: string;
  readonly descripcion?: string | null;
  readonly clasificacion: string;
  readonly retencionDias: number;
  readonly archivo: File;
}

/**
 * Progreso de una carga (`POST /api/v1/documentos` o `POST /api/v1/documentos/{id}/versiones`), derivado
 * de `HttpEvent<T>` real (`reportProgress: true`, `observe: 'events'`) -- NUNCA un progreso simulado con un
 * timer. `status: 'uploading'` puede repetirse muchas veces con `percent` creciente antes de un único
 * `status: 'done'` (o el observable falla, ver `DocumentUpload`).
 */
export type DocumentUploadProgress =
  | { readonly status: 'uploading'; readonly percent: number }
  | { readonly status: 'done'; readonly id: string };

/**
 * Consultas y mutaciones REALES sobre Documents (F7-10) -- envuelve exactamente los endpoints de
 * `DocumentsEndpointRouteBuilderExtensions` (`src/Platform/BitCode.Platform.Documents`, ver
 * `docs/guia-documents.md` para el detalle de cada uno). Multipart real (`FormData`) para las cargas,
 * `responseType: 'blob'` real para la descarga -- nunca JSON en ninguno de los dos casos.
 */
@Injectable({ providedIn: 'root' })
export class DocumentsService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/v1/documentos';

  /**
   * `POST /api/v1/documentos` (multipart/form-data) -- alta inicial de un documento junto con su versión
   * 1. Emite eventos de progreso reales mientras el cuerpo se transmite y termina con
   * `{ status: 'done', id }` (el `Guid` del documento creado, `201 Created`).
   */
  crear(input: CrearDocumentoInput): Observable<DocumentUploadProgress> {
    const formData = new FormData();
    formData.append('titulo', input.titulo);
    if (input.descripcion) {
      formData.append('descripcion', input.descripcion);
    }
    formData.append('clasificacion', input.clasificacion);
    formData.append('retencionDias', String(input.retencionDias));
    formData.append('archivo', input.archivo, input.archivo.name);

    return this.http
      .post<string>(this.baseUrl, formData, { reportProgress: true, observe: 'events' })
      .pipe(map((event) => this.toUploadProgress(event)));
  }

  /** `POST /api/v1/documentos/{id}/versiones` (multipart/form-data) -- nueva versión sobre un documento
   * existente. Mismo criterio de progreso real que `crear`; `id` en `{ status: 'done', id }` es el `Guid`
   * de la NUEVA VERSIÓN (no del documento, que ya se conoce -- es el mismo `id` recibido como parámetro). */
  subirVersion(documentoId: string, archivo: File): Observable<DocumentUploadProgress> {
    const formData = new FormData();
    formData.append('archivo', archivo, archivo.name);

    return this.http
      .post<string>(`${this.baseUrl}/${documentoId}/versiones`, formData, {
        reportProgress: true,
        observe: 'events',
      })
      .pipe(map((event) => this.toUploadProgress(event)));
  }

  obtener(id: string): Observable<DocumentoResponse> {
    return this.http.get<DocumentoResponse>(`${this.baseUrl}/${id}`);
  }

  listar(page: number, pageSize: number): Observable<DocumentosPagedResultDto> {
    const params = new HttpParams().set('page', page).set('pageSize', pageSize);
    return this.http.get<DocumentosPagedResultDto>(this.baseUrl, { params });
  }

  listarVersiones(documentoId: string): Observable<readonly DocumentoVersionResponse[]> {
    return this.http.get<readonly DocumentoVersionResponse[]>(`${this.baseUrl}/${documentoId}/versiones`);
  }

  /**
   * `GET /api/v1/documentos/{id}/descargar` -- descarga segura como blob real (`responseType: 'blob'`,
   * `observe: 'response'` para poder leer `Content-Disposition` y devolver el nombre de archivo real que
   * decidió el backend, no uno inventado en el cliente). `numero` ausente descarga la versión vigente. El
   * backend rechaza con 409 una versión `PendienteEscaneo`/`Infectado` (ver
   * `DocumentoVersion.PuedeDescargarse`) -- este método no reintenta ni oculta ese error, lo propaga tal
   * cual para que el consumidor lo mapee con `BitcodeErrorExperienceService` (F7-06).
   */
  descargar(id: string, numero?: number): Observable<{ blob: Blob; nombreArchivo: string }> {
    let params = new HttpParams();
    if (numero !== undefined) {
      params = params.set('numero', numero);
    }

    return this.http
      .get(`${this.baseUrl}/${id}/descargar`, { params, responseType: 'blob', observe: 'response' })
      .pipe(
        map((response) => ({
          blob: response.body ?? new Blob(),
          nombreArchivo: this.extractFileName(response.headers.get('content-disposition')) ?? 'documento',
        })),
      );
  }

  /** `DELETE /api/v1/documentos/{id}` -- disposición (baja lógica) tras vencer la retención. 409 si el
   * documento ya fue dispuesto o si la retención sigue vigente (`Documento.PuedeDisponerse`). */
  disponer(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }

  private toUploadProgress(event: HttpEvent<string>): DocumentUploadProgress {
    if (event.type === HttpEventType.UploadProgress) {
      const total = event.total ?? 0;
      const percent = total > 0 ? Math.round((event.loaded / total) * 100) : 0;
      return { status: 'uploading', percent };
    }
    if (event.type === HttpEventType.Response) {
      return { status: 'done', id: String(event.body) };
    }
    // Otros tipos de evento (Sent, ResponseHeader, etc.) no aportan progreso mostrable -- se degradan a
    // "uploading" con el último porcentaje conocido en 0 en vez de omitir el evento (mantiene el stream
    // vivo sin introducir un estado nuevo que el consumidor tendría que manejar).
    return { status: 'uploading', percent: 0 };
  }

  /** Extrae el nombre de archivo real de `Content-Disposition: attachment; filename="..."` (`Results.File`
   * del backend, ver `DocumentsEndpointRouteBuilderExtensions`). Degrada a `undefined` sin lanzar si el
   * header está ausente o no tiene la forma esperada. */
  private extractFileName(contentDisposition: string | null): string | undefined {
    if (!contentDisposition) {
      return undefined;
    }
    const match = /filename\*?=(?:UTF-8''|")?([^";]+)"?/i.exec(contentDisposition);
    return match ? decodeURIComponent(match[1]) : undefined;
  }
}
