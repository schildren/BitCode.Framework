using System.Reflection;
using BitCode.Framework.Shared.Application.Eventing;

namespace BitCode.Framework.Shared.Testing;

/// <summary>
/// Verifica, por reflexión sobre dos tipos concretos de <see cref="IIntegrationEvent"/>, si la
/// forma de la versión "actual" es compatible hacia atrás con la forma de la versión "anterior" —
/// instrumentación ejecutable de las reglas de <c>docs/politica-versionado.md</c> (sección 5,
/// "Versionado de eventos de dominio e integración") y <c>docs/guia-eventing-contratos.md</c>
/// (sección "Compatibilidad de esquema (F3-06)").
/// </summary>
/// <remarks>
/// <para>
/// Pensado como pieza reutilizable para que cada bounded context escriba, para sus propios eventos de
/// negocio, un test de contrato análogo a
/// <c>tests/Shared.Application.Tests/Eventing/EventSchemaCompatibilityTests.cs</c> (el "test de
/// referencia") — no requiere ningún broker ni infraestructura externa: opera únicamente sobre los
/// tipos .NET concretos del evento vía reflexión, sin necesitar una instancia serializada ni un
/// registro de esquema (Avro/Protobuf con Schema Registry sigue fuera de alcance, ver F3-02).
/// </para>
/// <para>
/// Reglas evaluadas (todas instrumentan <c>docs/politica-versionado.md</c>, sección 5):
/// </para>
/// <list type="bullet">
/// <item><description>Un campo de datos presente en la versión anterior que desaparece en la versión actual es una violación ("cambio breaking: eliminar un campo").</description></item>
/// <item><description>Un campo de datos presente en ambas versiones cuyo tipo CLR cambia es una violación ("cambio breaking: cambiar su tipo").</description></item>
/// <item><description>Un campo de datos nuevo en la versión actual que NO es nullable (ni <see cref="Nullable{T}"/> para tipos valor, ni una anotación de referencia nullable) es una violación ("agregar un campo requerido sin default" — un consumidor de la versión anterior no puede ignorarlo de forma segura).</description></item>
/// <item><description>Un campo de datos nuevo en la versión actual que SÍ es nullable no es una violación ("cambio aditivo: agregar un campo opcional").</description></item>
/// </list>
/// <para>
/// Los cuatro campos del envelope de <see cref="IIntegrationEvent"/> (<see cref="IIntegrationEvent.EventId"/>,
/// <see cref="IIntegrationEvent.OccurredOnUtc"/>, <see cref="IIntegrationEvent.EventType"/>,
/// <see cref="IIntegrationEvent.SchemaVersion"/>) quedan excluidos de la comparación: son parte del
/// contrato estable del contrato base (F3-01), no del payload de datos específico del evento concreto,
/// y <see cref="IIntegrationEvent.SchemaVersion"/> en particular cambia por diseño entre versiones sin
/// que eso sea, en sí mismo, una violación de compatibilidad.
/// </para>
/// <para>
/// Esta verificación NO reemplaza el criterio humano de si un cambio amerita incrementar
/// <see cref="IIntegrationEvent.SchemaVersion"/> — es la instrumentación mecánica de las reglas ya
/// documentadas, para que un cambio incompatible no pase inadvertido en CI sin que el autor lo haya
/// decidido explícitamente.
/// </para>
/// </remarks>
public static class EventSchemaCompatibilityChecker
{
    /// <summary>
    /// Compara la forma de <paramref name="currentVersion"/> contra <paramref name="previousVersion"/>
    /// y devuelve el resultado con la lista de violaciones encontradas (vacía si es compatible).
    /// </summary>
    /// <param name="previousVersion">Tipo concreto del evento en su versión anterior (por ejemplo, <c>SchemaVersion = 1</c>).</param>
    /// <param name="currentVersion">Tipo concreto del evento en su versión actual, a evaluar contra la anterior.</param>
    public static EventSchemaCompatibilityResult CheckBackwardCompatibility(Type previousVersion, Type currentVersion)
    {
        ArgumentNullException.ThrowIfNull(previousVersion);
        ArgumentNullException.ThrowIfNull(currentVersion);

        var previousProperties = GetDataProperties(previousVersion);
        var currentProperties = GetDataProperties(currentVersion);

        var violations = new List<string>();

        foreach (var (name, previousProperty) in previousProperties)
        {
            if (!currentProperties.TryGetValue(name, out var currentProperty))
            {
                violations.Add(
                    $"El campo '{name}' existe en {previousVersion.Name} y fue eliminado en {currentVersion.Name} " +
                    "(cambio breaking: eliminar un campo exige incrementar SchemaVersion, docs/politica-versionado.md secc. 5).");
                continue;
            }

            if (currentProperty.PropertyType != previousProperty.PropertyType)
            {
                violations.Add(
                    $"El campo '{name}' cambia de tipo ({previousProperty.PropertyType.Name} -> {currentProperty.PropertyType.Name}) " +
                    $"entre {previousVersion.Name} y {currentVersion.Name} " +
                    "(cambio breaking: cambiar el tipo de un campo exige incrementar SchemaVersion, docs/politica-versionado.md secc. 5).");
            }
        }

        foreach (var (name, currentProperty) in currentProperties)
        {
            if (previousProperties.ContainsKey(name))
            {
                continue;
            }

            if (!IsOptional(currentProperty))
            {
                violations.Add(
                    $"El campo '{name}', agregado en {currentVersion.Name}, es requerido (no nullable) — " +
                    $"un consumidor de {previousVersion.Name} no puede ignorarlo de forma segura sin incrementar " +
                    "SchemaVersion (docs/politica-versionado.md secc. 5: 'agregar un campo requerido sin default' es breaking).");
            }
        }

        return new EventSchemaCompatibilityResult(violations);
    }

    private static Dictionary<string, PropertyInfo> GetDataProperties(Type eventType)
    {
        var envelopeProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(IIntegrationEvent.EventId),
            nameof(IIntegrationEvent.OccurredOnUtc),
            nameof(IIntegrationEvent.EventType),
            nameof(IIntegrationEvent.SchemaVersion),
            // Miembro generado por el compilador para todo `record`, no es un campo de datos del evento.
            "EqualityContract",
        };

        return eventType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .Where(property => !envelopeProperties.Contains(property.Name))
            .ToDictionary(property => property.Name, property => property, StringComparer.Ordinal);
    }

    private static bool IsOptional(PropertyInfo property)
    {
        if (Nullable.GetUnderlyingType(property.PropertyType) is not null)
        {
            return true;
        }

        if (property.PropertyType.IsValueType)
        {
            return false;
        }

        var nullabilityInfo = new NullabilityInfoContext().Create(property);
        return nullabilityInfo.ReadState == NullabilityState.Nullable;
    }
}

/// <summary>Resultado de <see cref="EventSchemaCompatibilityChecker.CheckBackwardCompatibility"/>.</summary>
/// <param name="Violations">Violaciones encontradas, en texto legible listo para reportar en un mensaje de aserción; vacío si la evolución es compatible.</param>
public sealed record EventSchemaCompatibilityResult(IReadOnlyList<string> Violations)
{
    /// <summary>Verdadero si no se encontró ninguna violación de compatibilidad hacia atrás.</summary>
    public bool IsBackwardCompatible => Violations.Count == 0;
}
