namespace BitCode.Framework.Platform.Identity.Users;

internal sealed record UsuarioResponse(
    Guid Id,
    string? UserName,
    string? Email,
    bool Bloqueado,
    IReadOnlyList<string> Roles);
