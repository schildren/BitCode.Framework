namespace BitCode.Framework.Shared.Infrastructure.Security.Abac;

/// <summary>
/// Una regla de negocio basada en atributos (F2-08, ABAC) -- por ejemplo, "el monto del recurso no
/// puede superar el límite del sujeto" o "el recurso debe pertenecer a una empresa/sucursal a la que el
/// sujeto tiene acceso". <see cref="AuthorizationPolicyEvaluator"/> ejecuta todas las reglas registradas
/// que <see cref="AppliesTo"/> acepte para el (tipo de recurso, acción) de la evaluación actual, DESPUÉS
/// de confirmar que RBAC ya concedió el permiso base -- una regla nunca se evalúa si RBAC ya denegó, y
/// su único efecto posible es restringir (nunca conceder), ver <see cref="AbacRuleOutcome"/>.
/// El framework incluye dos implementaciones genéricas y configurables por atributos
/// (<see cref="AttributeScopeAbacRule"/> para empresa/sucursal, <see cref="AmountLimitAbacRule"/> para
/// monto) que cubren el criterio de aceptación literal de F2-08 sin que el proyecto consumidor escriba
/// código; un proyecto con una regla de negocio más compleja registra su propia implementación de esta
/// interfaz (<c>services.AddScoped&lt;IAbacRule, MiRegla&gt;()</c>, se suma a las incorporadas).
/// </summary>
public interface IAbacRule
{
    /// <summary>
    /// Indica si esta regla tiene algo que evaluar para el tipo de recurso y la acción dados -- permite
    /// que <see cref="AuthorizationPolicyEvaluator"/> se salte reglas irrelevantes sin ejecutar
    /// <see cref="EvaluateAsync"/> innecesariamente (por ejemplo, una regla de límite de monto
    /// configurada solo para <c>"pedidos"</c> no corre para una evaluación de <c>"productos"</c>).
    /// </summary>
    bool AppliesTo(string resourceType, string action);

    Task<AbacRuleOutcome> EvaluateAsync(
        AbacSubject subject,
        AbacResource resource,
        string action,
        AbacContext context,
        CancellationToken cancellationToken = default);
}
