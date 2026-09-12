namespace BitCode.Framework.Shared.Domain.MultiTenancy;

/// <summary>
/// Nombre del claim JWT que transporta el <c>TenantId</c> del usuario autenticado (F1-12).
/// <see cref="TenantId"/> lo emite <c>JwtTokenGenerator</c> (Shared.Infrastructure.Security) y lo
/// consume la implementación productiva de <see cref="ITenantProvider"/>
/// (<c>HttpContextTenantProvider</c>, Shared.Infrastructure.Web). El TenantId nunca debe resolverse
/// desde un header HTTP o un query string que el cliente controle directamente sin pasar por
/// autenticación validada por el servidor — ver <c>docs/threat-model.md</c>, hallazgo S2.
/// </summary>
public static class TenantClaimTypes
{
    public const string TenantId = "tenant_id";
}
