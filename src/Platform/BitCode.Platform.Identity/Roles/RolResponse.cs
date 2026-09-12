namespace BitCode.Framework.Platform.Identity.Roles;

internal sealed record RolResponse(Guid Id, string NombreRol, IReadOnlyList<string> Permisos);
