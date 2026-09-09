namespace BitCode.Framework.Platform.IntegrationHub.Conectores;

/// <summary>Nunca expone <see cref="IntegrationConnector.SecretKey"/> -- ver el <c>remarks</c> de
/// <see cref="IntegrationConnector"/>: aunque solo sea una referencia (no el valor del secreto), no hay
/// ninguna razón para que un cliente HTTP que solo necesita administrar/consultar conectores la reciba.</summary>
internal sealed record ConectorResponse(
    Guid Id,
    string Codigo,
    string Nombre,
    string BaseUrl,
    MetodoHttpConector Metodo,
    TipoAutenticacionConector TipoAutenticacion,
    string? ApiKeyHeaderName,
    bool Activo,
    IReadOnlyList<FieldMappingResponse> Mappings);

internal sealed record FieldMappingResponse(Guid Id, string CampoOrigen, string CampoDestino);

internal sealed record ConectorResumenResponse(
    Guid Id, string Codigo, string Nombre, MetodoHttpConector Metodo, TipoAutenticacionConector TipoAutenticacion, bool Activo);
