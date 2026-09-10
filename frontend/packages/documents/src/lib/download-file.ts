/**
 * Dispara la descarga de un `Blob` ya recibido del backend (`DocumentsService.descargar`) como un archivo
 * en el navegador -- crea un `<a>` temporal con `URL.createObjectURL`, simula el click y libera el object
 * URL inmediatamente después (`revokeObjectURL`) para no filtrar memoria en sesiones largas con muchas
 * descargas. No hace ningún request HTTP por sí mismo: separado de `DocumentsService.descargar` a
 * propósito para que un consumidor pueda decidir hacer algo distinto con el blob (previsualizarlo,
 * verificarlo) antes de guardarlo.
 */
export function downloadFile(blob: Blob, nombreArchivo: string): void {
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = nombreArchivo;
  anchor.style.display = 'none';
  document.body.appendChild(anchor);
  anchor.click();
  document.body.removeChild(anchor);
  URL.revokeObjectURL(url);
}
