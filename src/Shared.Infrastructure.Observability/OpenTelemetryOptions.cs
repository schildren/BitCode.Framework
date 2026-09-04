namespace BitCode.Framework.Shared.Infrastructure.Observability;

public class OpenTelemetryOptions
{
    public const string SectionName = "OpenTelemetry";

    public required string ServiceName { get; set; }

    /// <summary>Endpoint OTLP (p.ej. http://localhost:4317). Si es null/vacío, no se exporta —
    /// útil en desarrollo local sin collector, sin que el registro falle.</summary>
    public string? OtlpEndpoint { get; set; }
}
