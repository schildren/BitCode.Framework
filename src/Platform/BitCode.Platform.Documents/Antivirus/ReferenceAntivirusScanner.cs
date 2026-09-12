using System.Text;
using BitCode.Framework.Platform.Documents.Documentos;

namespace BitCode.Framework.Platform.Documents.Antivirus;

/// <summary>
/// Implementación de referencia de <see cref="IAntivirusScanner"/> para desarrollo/pruebas/demos --
/// NUNCA usar en un despliegue productivo real (no analiza el contenido más allá de buscar el string de
/// prueba estándar de la industria antivirus, ver <see cref="EicarSignature"/>). Un consumidor real
/// reemplaza el registro de <see cref="IAntivirusScanner"/> con un adapter contra un motor real (ClamAV
/// vía socket Unix/TCP, Windows Defender API, un servicio de escaneo cloud, etc.) -- ningún handler de
/// este módulo cambia (mismo criterio de desacoplamiento que <see cref="Almacenamiento.IDocumentBlobStore"/>).
/// </summary>
public sealed class ReferenceAntivirusScanner : IAntivirusScanner
{
    /// <summary>El "EICAR Standard Anti-Virus Test File" -- una cadena de 68 bytes que todo motor
    /// antivirus real reconoce como "virus de prueba" sin ser código malicioso, estándar de facto de la
    /// industria desde 1990 (European Institute for Computer Antivirus Research) para probar
    /// integraciones sin manejar malware real.</summary>
    public const string EicarSignature = @"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*";

    public Task<EstadoEscaneo> ScanAsync(byte[] content, CancellationToken cancellationToken = default)
    {
        var texto = Encoding.ASCII.GetString(content);
        var resultado = texto.Contains(EicarSignature, StringComparison.Ordinal)
            ? EstadoEscaneo.Infectado
            : EstadoEscaneo.Limpio;

        return Task.FromResult(resultado);
    }
}
