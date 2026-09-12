namespace BitCode.Framework.Platform.Catalogs.Parametros;

internal sealed record ParametroVigenciaResponse(Guid Id, Guid ParametroId, string Valor, DateTime VigenteDesde, DateTime? VigenteHasta);

/// <summary>Respuesta de <c>ObtenerValorVigenteQuery</c> -- distinta de <see cref="ParametroVigenciaResponse"/>
/// porque incluye la fecha de consulta usada para resolver la vigencia (auditable/depurable por el
/// cliente), aunque el payload de datos sea equivalente.</summary>
internal sealed record ParametroValorVigenteResponse(Guid ParametroId, string Valor, DateTime Fecha, DateTime VigenteDesde, DateTime? VigenteHasta);
