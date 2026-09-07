namespace BitCode.Framework.Shared.Infrastructure.Messaging.Kafka;

/// <summary>
/// Resuelve el nombre de tópico Kafka a partir de <see cref="BitCode.Framework.Shared.Application.Eventing.IIntegrationEvent.EventType"/>.
/// </summary>
/// <remarks>
/// Punto de extensión deliberado para un futuro esquema de tópicos por ambiente/tenant: F3-02 solo
/// resuelve el criterio de aceptación mínimo ("el nombre de tópico puede derivarse de EventType por
/// ahora"), sin definir un esquema de tópicos por tenant/ambiente — eso excede el alcance de esta tarea.
/// La clave de PARTICIÓN dentro de un mismo tópico (AggregateId/TenantId según orden requerido) es F3-05
/// (<c>IHasPartitionKey</c>, <c>Shared.Application.Eventing</c>) — un concepto distinto de a qué tópico
/// se enruta un evento, que sigue siendo responsabilidad exclusiva de este resolver.
/// </remarks>
public interface IKafkaTopicNameResolver
{
    /// <summary>Nombre del tópico Kafka correspondiente a <paramref name="eventType"/>.</summary>
    string ResolveTopicName(string eventType);
}

/// <summary>
/// Resolución por defecto: usa <c>EventType</c> tal cual como nombre de tópico (p. ej.
/// <c>"Pedidos.PedidoCreado"</c> ya es un nombre de tópico Kafka válido: letras, números, '.', '_' y
/// '-'), reemplazando únicamente caracteres no válidos para un nombre de tópico Kafka por '_'.
/// </summary>
public sealed class DefaultKafkaTopicNameResolver : IKafkaTopicNameResolver
{
    public static readonly DefaultKafkaTopicNameResolver Instance = new();

    public string ResolveTopicName(string eventType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);

        Span<char> buffer = eventType.Length <= 256 ? stackalloc char[eventType.Length] : new char[eventType.Length];
        for (var i = 0; i < eventType.Length; i++)
        {
            var c = eventType[i];
            buffer[i] = char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_';
        }

        return new string(buffer);
    }
}
