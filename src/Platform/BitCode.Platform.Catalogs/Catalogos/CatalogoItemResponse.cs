namespace BitCode.Framework.Platform.Catalogs.Catalogos;

internal sealed record CatalogoItemResponse(Guid Id, string Codigo, string Etiqueta, string? Valor, int Orden);
