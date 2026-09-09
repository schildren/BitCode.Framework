namespace BitCode.Framework.Platform.Documents.Documentos;

public sealed record DocumentoResponse(
    Guid Id,
    string Titulo,
    string? Descripcion,
    string Clasificacion,
    int RetencionDias,
    Guid VersionActualId,
    int VersionActualNumero,
    DateTime DisponibleParaDisposicionDesde);

public sealed record DocumentoVersionResponse(
    Guid Id,
    Guid DocumentoId,
    int Numero,
    string NombreArchivo,
    string ContentType,
    long TamanioBytes,
    string HashSha256,
    EstadoEscaneo EstadoEscaneo);
