namespace BitCode.Framework.Shared.Infrastructure.Security.Abac;

/// <summary>
/// Configuración de las dos <see cref="IAbacRule"/> genéricas que trae el framework (F2-08):
/// <see cref="AttributeScopeAbacRule"/> (empresa/sucursal/cualquier atributo de alcance) y
/// <see cref="AmountLimitAbacRule"/> (monto). Ninguna de las dos viene con reglas por defecto -- una
/// lista vacía significa que esa regla nunca deniega (siempre <c>NotApplicable</c>); cada proyecto
/// declara las suyas según su propio dominio, típicamente en <c>InfrastructureModule</c>:
/// <code>
/// services.AddSharedAbacAuthorization(options =>
/// {
///     options.ScopeRules.Add(new AbacScopeAttributeRule
///     {
///         ResourceType = "pedidos",
///         ResourceAttributeKey = "empresaId",
///         ClaimType = "empresa_id",
///     });
///     options.AmountLimitRules.Add(new AbacAmountLimitRule
///     {
///         ResourceType = "pedidos",
///         ResourceAttributeKey = "monto",
///         ClaimType = "monto_maximo",
///     });
/// });
/// </code>
/// </summary>
public sealed class AbacOptions
{
    public IList<AbacScopeAttributeRule> ScopeRules { get; } = [];

    public IList<AbacAmountLimitRule> AmountLimitRules { get; } = [];
}

/// <summary>
/// Una restricción de alcance por atributo (F2-08): el valor de <see cref="ResourceAttributeKey"/> en
/// el recurso evaluado debe figurar entre los valores del claim <see cref="ClaimType"/> del sujeto --
/// el patrón genérico detrás de "empresa" y "sucursal" (dos instancias de esta misma regla, una por
/// atributo), pero aplicable a cualquier atributo de alcance que un proyecto necesite restringir de la
/// misma forma. Ver <see cref="AttributeScopeAbacRule"/> para el algoritmo exacto (incluida la política
/// de "sin dato, no se restringe").
/// </summary>
public sealed class AbacScopeAttributeRule
{
    /// <summary>Tipo de recurso al que aplica esta restricción, o <c>"*"</c> para todos.</summary>
    public required string ResourceType { get; init; }

    /// <summary>Clave en <see cref="AbacResource.Attributes"/> que transporta el valor de alcance del
    /// recurso (por ejemplo, <c>"empresaId"</c>).</summary>
    public required string ResourceAttributeKey { get; init; }

    /// <summary>Claim del sujeto cuyos valores representan el alcance permitido (por ejemplo,
    /// <c>"empresa_id"</c>). Puede tener más de un valor (varias empresas/sucursales).</summary>
    public required string ClaimType { get; init; }
}

/// <summary>
/// Un límite numérico por atributo (F2-08): el valor de <see cref="ResourceAttributeKey"/> en el
/// recurso evaluado no puede superar el valor del claim <see cref="ClaimType"/> del sujeto -- el
/// patrón genérico detrás de "monto" (un límite de importe por sujeto/rol), aplicable a cualquier
/// atributo numérico que un proyecto necesite acotar de la misma forma. Ver
/// <see cref="AmountLimitAbacRule"/> para el algoritmo exacto (incluida la política de "sin dato, no se
/// restringe").
/// </summary>
public sealed class AbacAmountLimitRule
{
    /// <summary>Tipo de recurso al que aplica esta restricción, o <c>"*"</c> para todos.</summary>
    public required string ResourceType { get; init; }

    /// <summary>Clave en <see cref="AbacResource.Attributes"/> que transporta el importe del recurso
    /// (por ejemplo, <c>"monto"</c>).</summary>
    public string ResourceAttributeKey { get; init; } = "monto";

    /// <summary>Claim del sujeto cuyo valor es el límite máximo permitido para este importe (por
    /// ejemplo, <c>"monto_maximo"</c>).</summary>
    public required string ClaimType { get; init; }
}
