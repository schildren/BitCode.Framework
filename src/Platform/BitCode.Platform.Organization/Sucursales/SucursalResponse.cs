namespace BitCode.Framework.Platform.Organization.Sucursales;

internal sealed record SucursalResponse(Guid Id, Guid EmpresaId, string Nombre, string? Direccion, bool Activa);
