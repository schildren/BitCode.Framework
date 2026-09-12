namespace BitCode.Framework.Shared.Infrastructure.Security.Oidc.ServiceIdentity;

/// <summary>
/// Token de acceso de la identidad propia de este workload, obtenido vía Client Credentials (F2-04).
/// <see cref="ExpiresAtUtc"/> es el dato que <see cref="ServiceTokenCache"/> usa para decidir cuándo
/// renovar -- nunca se reutiliza un token vencido ni se calcula la vigencia en el punto de consumo
/// (<see cref="ServiceIdentityAuthenticationHandler"/>).
/// </summary>
public sealed record ServiceAccessToken(string AccessToken, string TokenType, DateTimeOffset ExpiresAtUtc);
