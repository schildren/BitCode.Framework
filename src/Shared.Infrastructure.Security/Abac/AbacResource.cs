namespace BitCode.Framework.Shared.Infrastructure.Security.Abac;

/// <summary>
/// El "Resource" del modelo ABAC (F2-08): el objeto de negocio sobre el que se pide autorización,
/// identificado por <see cref="Type"/> (el mismo segmento de entidad usado por el permiso RBAC
/// equivalente, <c>"{entidad}.{accion}"</c> -- ver <c>docs/convenciones.md</c>) y descripto por
/// <see cref="Attributes"/> -- los datos de negocio concretos de ESTA instancia (por ejemplo, el monto,
/// la empresa o la sucursal de un pedido puntual) que <see cref="IAbacRule"/> necesita para decidir. A
/// diferencia de un permiso RBAC (estático, "puede crear pedidos"), estos atributos solo se conocen en
/// tiempo de ejecución -- el llamador (típicamente un handler de aplicación, después de leer la entidad)
/// los arma explícitamente; no hay ninguna resolución automática desde el request HTTP.
/// </summary>
public sealed class AbacResource
{
    public AbacResource(string type, IReadOnlyDictionary<string, object?>? attributes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);

        Type = type;
        Attributes = attributes ?? new Dictionary<string, object?>();
    }

    /// <summary>
    /// El tipo de recurso (por ejemplo, <c>"pedidos"</c>). Combinado con la acción
    /// (<see cref="IAuthorizationPolicyEvaluator.EvaluateAsync"/>) compone el permiso RBAC requerido
    /// como base de la decisión (<c>"{Type}.{action}"</c>).
    /// </summary>
    public string Type { get; }

    /// <summary>
    /// Atributos de negocio de esta instancia concreta del recurso (monto, empresaId, sucursalId, u
    /// otro atributo que un <see cref="IAbacRule"/> del proyecto consumidor necesite). Sin entradas
    /// predefinidas por el framework -- el conjunto de claves lo define cada proyecto.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Attributes { get; }
}
