namespace BitCode.Framework.Shared.Infrastructure.Security.Abac;

/// <summary>
/// El "Context"/"Environment" del modelo ABAC (F2-08): atributos ajenos tanto al sujeto como al recurso
/// -- por ejemplo, la hora del día, la IP de origen o cualquier otra condición ambiental que un
/// <see cref="IAbacRule"/> del proyecto consumidor necesite. Ninguna regla incorporada por el framework
/// (<see cref="AttributeScopeAbacRule"/>/<see cref="AmountLimitAbacRule"/>) lee este objeto hoy; existe
/// para que una regla propia del proyecto no necesite un contrato ad-hoc para recibir ese tipo de dato.
/// </summary>
public sealed class AbacContext
{
    public static AbacContext Empty { get; } = new();

    public AbacContext(IReadOnlyDictionary<string, object?>? attributes = null)
    {
        Attributes = attributes ?? new Dictionary<string, object?>();
    }

    public IReadOnlyDictionary<string, object?> Attributes { get; }
}
