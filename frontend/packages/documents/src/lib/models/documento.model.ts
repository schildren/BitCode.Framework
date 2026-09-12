/**
 * Estado de escaneo antivirus de una `DocumentoVersion` -- valores verificados 1:1 contra
 * `BitCode.Framework.Platform.Documents.Documentos.EstadoEscaneo`
 * (`src/Platform/BitCode.Platform.Documents/Documentos/DocumentoVersion.cs`). Una versión nace SIEMPRE en
 * `PendienteEscaneo`; nunca puede descargarse hasta que quede `Limpio` (ver
 * `docs/guia-documents.md` y `DescargarDocumentoVersionQueryHandler` en el backend).
 */
export enum EstadoEscaneo {
  PendienteEscaneo = 0,
  Limpio = 1,
  Infectado = 2,
}

const ESTADO_ESCANEO_LABELS: Readonly<Record<EstadoEscaneo, string>> = {
  [EstadoEscaneo.PendienteEscaneo]: 'Escaneo pendiente',
  [EstadoEscaneo.Limpio]: 'Limpio',
  [EstadoEscaneo.Infectado]: 'Infectado',
};

export function estadoEscaneoLabel(estado: EstadoEscaneo): string {
  return ESTADO_ESCANEO_LABELS[estado] ?? `Desconocido (${estado})`;
}

/** `true` únicamente cuando el backend permitiría descargar esta versión (`PuedeDescargarse()` en
 * `DocumentoVersion.cs`) -- una versión `PendienteEscaneo` o `Infectado` nunca se ofrece para descarga en
 * la UI, aunque el usuario tenga el permiso `documentos.descargar` (el backend igual la rechazaría con
 * 409). */
export function puedeDescargarse(estado: EstadoEscaneo): boolean {
  return estado === EstadoEscaneo.Limpio;
}

/**
 * Metadata de un documento -- shape verificada 1:1 contra `DocumentoResponse`
 * (`src/Platform/BitCode.Platform.Documents/Documentos/DocumentoResponse.cs`), tal como la devuelve
 * `GET /api/v1/documentos/{id}` y `GET /api/v1/documentos` (paginado). El contenido real NUNCA viaja acá
 * -- sólo el puntero a la versión vigente (`VersionActualId`/`VersionActualNumero`, ver
 * `DocumentoVersion`).
 */
export interface DocumentoResponse {
  readonly id: string;
  readonly titulo: string;
  readonly descripcion?: string | null;
  readonly clasificacion: string;
  readonly retencionDias: number;
  readonly versionActualId: string;
  readonly versionActualNumero: number;
  readonly disponibleParaDisposicionDesde: string;
}

/**
 * Una versión concreta e INMUTABLE del contenido de un documento -- shape verificada 1:1 contra
 * `DocumentoVersionResponse` (mismo archivo), tal como la devuelve
 * `GET /api/v1/documentos/{id}/versiones`. `hashSha256` es el hash del contenido en hexadecimal
 * mayúsculas (`Convert.ToHexString` del backend) -- este paquete no lo recalcula ni lo verifica (ver
 * `docs/guia-frontend-documents.md`, "Pendientes").
 */
export interface DocumentoVersionResponse {
  readonly id: string;
  readonly documentoId: string;
  readonly numero: number;
  readonly nombreArchivo: string;
  readonly contentType: string;
  readonly tamanioBytes: number;
  readonly hashSha256: string;
  readonly estadoEscaneo: EstadoEscaneo;
}
