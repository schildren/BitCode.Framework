namespace BitCode.Framework.Platform.Catalogs.Catalogos;

internal sealed record CatalogoVersionResponse(
    Guid Id, Guid CatalogoId, int Numero, CatalogoVersionEstado Estado, DateTime? VigenteDesde, DateTime? VigenteHasta);
