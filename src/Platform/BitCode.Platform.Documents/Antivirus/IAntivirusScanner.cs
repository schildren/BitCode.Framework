using BitCode.Framework.Platform.Documents.Documentos;

namespace BitCode.Framework.Platform.Documents.Antivirus;

/// <summary>
/// Abstracción de escaneo antivirus (Épica de Documents: "Escaneo antivirus"). NO hay integración real
/// con un motor antivirus (ClamAV, Windows Defender, un servicio de escaneo cloud, etc.) en este primer
/// corte -- eso es infraestructura externa (proceso separado, licenciamiento, actualización de firmas)
/// que excede el alcance de este corte de plataforma. La implementación de referencia registrada por
/// defecto (<see cref="ReferenceAntivirusScanner"/>) reconoce el string de prueba estándar EICAR (el
/// mismo usado por la industria para probar integraciones antivirus sin un virus real) -- suficiente para
/// ejercitar de punta a punta el flujo de negocio real ("una versión Infectada nunca se descarga") sin
/// depender de un motor externo. Ver <c>docs/guia-documents.md</c>, sección "Pendientes", para el
/// pendiente explícito de un adapter real.
/// </summary>
public interface IAntivirusScanner
{
    Task<EstadoEscaneo> ScanAsync(byte[] content, CancellationToken cancellationToken = default);
}
