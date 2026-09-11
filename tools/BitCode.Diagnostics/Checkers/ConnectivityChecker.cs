using System.Diagnostics;
using System.Net.Sockets;
using BitCode.Framework.Tools.Diagnostics.Core;

namespace BitCode.Framework.Tools.Diagnostics.Checkers;

/// <summary>
/// Valida la conectividad de red y disponibilidad de dependencias de infraestructura local o remota.
/// </summary>
public sealed class ConnectivityChecker : IDiagnosticChecker
{
    private readonly TimeSpan _timeout;

    public ConnectivityChecker(TimeSpan? timeout = null)
    {
        _timeout = timeout ?? TimeSpan.FromSeconds(2);
    }

    public DiagnosticCategory Category => DiagnosticCategory.Connectivity;

    public async Task<IReadOnlyList<DiagnosticItem>> RunChecksAsync(CancellationToken cancellationToken = default)
    {
        var tasks = new[]
        {
            CheckTcpServiceAsync(
                "SQL Server 2022",
                "127.0.0.1",
                1433,
                "Base de datos relacional principal",
                "Inicie el contenedor con: .\\scripts\\dev-env.ps1 up (o docker compose up -d sqlserver).",
                cancellationToken),

            CheckTcpServiceAsync(
                "Redis 7.2",
                "127.0.0.1",
                6379,
                "Cache distribuido L2",
                "Inicie el contenedor con: .\\scripts\\dev-env.ps1 up (o docker compose up -d redis).",
                cancellationToken),

            CheckTcpServiceAsync(
                "Apache Kafka (KRaft)",
                "127.0.0.1",
                9092,
                "Broker de eventos de integración",
                "Inicie el contenedor con: .\\scripts\\dev-env.ps1 up (o docker compose up -d kafka).",
                cancellationToken),

            CheckHttpEndpointAsync(
                "OpenTelemetry Collector",
                "http://127.0.0.1:13133/",
                "Pipeline local de telemetría y diagnóstico",
                "Inicie el colector con: .\\scripts\\dev-env.ps1 up (o docker compose up -d otel-collector).",
                cancellationToken),

            CheckHttpEndpointAsync(
                "Jaeger Tracing UI",
                "http://127.0.0.1:16686/",
                "Interfaz web de trazabilidad distribuida",
                "Inicie Jaeger con: .\\scripts\\dev-env.ps1 up (o docker compose up -d jaeger).",
                cancellationToken)
        };

        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }

    public async Task<DiagnosticItem> CheckTcpServiceAsync(
        string serviceName,
        string host,
        int port,
        string description,
        string remediation,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var client = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_timeout);

            await client.ConnectAsync(host, port, cts.Token);
            sw.Stop();

            return DiagnosticItem.Success(
                DiagnosticCategory.Connectivity,
                serviceName,
                $"{serviceName} responde correctamente ({description}).",
                $"{host}:{port} — Latencia: {sw.ElapsedMilliseconds} ms");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return DiagnosticItem.Warning(
                DiagnosticCategory.Connectivity,
                serviceName,
                $"No se pudo conectar a {serviceName} en {host}:{port}.",
                $"Endpoint: {host}:{port} — Error: {ex.Message} ({sw.ElapsedMilliseconds} ms)",
                remediation);
        }
    }

    public async Task<DiagnosticItem> CheckHttpEndpointAsync(
        string serviceName,
        string url,
        string description,
        string remediation,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var httpClient = new HttpClient { Timeout = _timeout };
            using var response = await httpClient.GetAsync(url, cancellationToken);
            sw.Stop();

            if (response.IsSuccessStatusCode)
            {
                return DiagnosticItem.Success(
                    DiagnosticCategory.Connectivity,
                    serviceName,
                    $"{serviceName} responde HTTP {(int)response.StatusCode} ({description}).",
                    $"{url} — Latencia: {sw.ElapsedMilliseconds} ms");
            }

            return DiagnosticItem.Warning(
                DiagnosticCategory.Connectivity,
                serviceName,
                $"{serviceName} devolvió código HTTP {(int)response.StatusCode} {response.ReasonPhrase}.",
                $"Endpoint: {url} — Latencia: {sw.ElapsedMilliseconds} ms",
                remediation);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return DiagnosticItem.Warning(
                DiagnosticCategory.Connectivity,
                serviceName,
                $"Endpoint {serviceName} ({url}) no responde.",
                $"Endpoint: {url} — Error: {ex.Message} ({sw.ElapsedMilliseconds} ms)",
                remediation);
        }
    }
}
