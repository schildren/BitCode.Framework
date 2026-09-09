namespace BitCode.Framework.Platform.FeatureManagement.Segmentos;

public sealed record SegmentoResponse(Guid Id, string Nombre, SegmentoTipo Tipo, Guid? TenantIdCriterio, int? Porcentaje);
