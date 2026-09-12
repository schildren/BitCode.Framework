namespace BitCode.Framework.Platform.Organization.Areas;

internal sealed record AreaResponse(Guid Id, Guid SucursalId, string Nombre, Guid? ParentAreaId);
