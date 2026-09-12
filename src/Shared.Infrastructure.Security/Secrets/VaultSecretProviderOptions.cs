namespace BitCode.Framework.Shared.Infrastructure.Security.Secrets;

/// <summary>
/// Opciones del proveedor de secretos sobre HashiCorp Vault (<see cref="VaultSecretProvider"/>, ADR 0014
/// -- proveedor propuesto, pendiente de aprobación humana explícita antes de habilitarse en producción,
/// Plan Maestro sección 13). Todos los valores obligatorios de esta sección (en particular
/// <see cref="Token"/>) deben resolverse en el entorno real vía variable de entorno o el propio mecanismo
/// de arranque del proceso -- nunca como literal en <c>appsettings.json</c> (F2-12, "Cero secretos en
/// repositorio": el "secreto para llegar al secreto" tampoco puede vivir en el repositorio).
/// </summary>
public sealed class VaultSecretProviderOptions
{
    public const string SectionName = "Secrets:Vault";

    /// <summary>
    /// URL base del servidor Vault, por ejemplo <c>"https://vault.local:8200"</c>. Zero Trust (Plan
    /// Maestro sección 1): debe ser HTTPS en cualquier entorno real; solo se acepta HTTP en Testcontainers
    /// (dev mode del contenedor de pruebas, sin TLS) o cuando <see cref="AllowInsecureHttp"/> se activa
    /// explícitamente para desarrollo local.
    /// </summary>
    public required string Address { get; set; }

    /// <summary>
    /// Token de autenticación contra Vault (método de autenticación "token" -- el más simple soportado
    /// por Vault, documentado como punto de partida; AppRole/Kubernetes auth quedan fuera de alcance de
    /// F2-12, ver <c>docs/guia-secret-provider.md</c>, "queda fuera de alcance"). Se resuelve desde
    /// configuración (típicamente una variable de entorno, <c>Secrets__Vault__Token</c>), nunca
    /// hardcodeado.
    /// </summary>
    public required string Token { get; set; }

    /// <summary>
    /// Punto de montaje del motor de secretos KV versión 2, por ejemplo <c>"secret"</c> (default estándar
    /// de Vault en modo dev).
    /// </summary>
    public string MountPath { get; set; } = "secret";

    /// <summary>
    /// Prefijo de ruta dentro del motor KV donde viven los secretos de este proyecto (aislamiento lógico
    /// entre proyectos/equipos que comparten el mismo Vault), por ejemplo <c>"bitcode"</c>. Una clave
    /// pedida a <see cref="VaultSecretProvider.GetSecretAsync"/> con valor <c>"mi-clave"</c> resuelve a
    /// <c>"{MountPath}/data/{PathPrefix}/mi-clave"</c> (API KV v2 de Vault).
    /// </summary>
    public string PathPrefix { get; set; } = string.Empty;

    /// <summary>
    /// Nombre del campo dentro del secreto KV v2 que contiene el valor efectivo, default <c>"value"</c>
    /// (convención de este provider: un secreto simple se guarda como <c>{"value": "..."}</c>). Un secreto
    /// con múltiples campos puede seguir leyéndose ajustando este nombre por instancia de opciones si el
    /// consumidor lo requiere.
    /// </summary>
    public string ValueFieldName { get; set; } = "value";

    /// <summary>
    /// Permite explícitamente <see cref="Address"/> sin HTTPS fuera de Testcontainers -- solo para
    /// desarrollo local contra un Vault en modo dev sin TLS configurado. Falso por defecto (Zero Trust).
    /// </summary>
    public bool AllowInsecureHttp { get; set; }
}
