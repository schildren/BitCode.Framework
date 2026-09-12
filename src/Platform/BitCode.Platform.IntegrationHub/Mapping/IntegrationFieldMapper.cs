using System.Text.Json;
using System.Text.Json.Nodes;
using BitCode.Framework.Platform.IntegrationHub.Conectores;
using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Platform.IntegrationHub.Mapping;

/// <summary>
/// Motor de mapping campo-a-campo entre el payload interno y el payload externo de un
/// <see cref="IntegrationConnector"/> (Fase 6, módulo 9: "mapping" del Plan Maestro).
/// DELIBERADAMENTE simple -- ver el <c>remarks</c> de <see cref="IntegrationFieldMapping"/> para por qué
/// esta tarea no introduce un motor de transformación genérico.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IntegrationFieldMapping.CampoOrigen"/>/<see cref="IntegrationFieldMapping.CampoDestino"/>
/// son rutas separadas por punto sobre un objeto JSON anidado (por ejemplo <c>"cliente.nombre"</c>) --
/// SIN soporte de índices de array (<c>"items[0].sku"</c>), SIN wildcards y SIN ninguna conversión de
/// tipo (el valor se copia tal cual, con su tipo JSON original: string/number/bool/null/objeto/array
/// completo). Un campo origen faltante en el payload interno se OMITE en el payload externo (no produce
/// error ni un valor <c>null</c> explícito) -- el llamador es responsable de que su payload interno
/// siempre incluya los campos que su mapping declara si el sistema externo los requiere.
/// </para>
/// <para>
/// Un mapping vacío (sin filas) produce un payload externo <c>"{}"</c> -- un conector sin ningún
/// <see cref="IntegrationFieldMapping"/> configurado envía siempre un objeto JSON vacío, nunca el
/// payload interno "tal cual" (no hay un modo "passthrough" implícito; si un consumidor real necesita
/// enviar el payload interno sin transformar, debe declarar un mapping 1:1 explícito campo por campo).
/// </para>
/// </remarks>
public static class IntegrationFieldMapper
{
    public static Result<string> Map(string payloadInternoJson, IReadOnlyList<IntegrationFieldMapping> mappings)
    {
        ArgumentNullException.ThrowIfNull(mappings);

        JsonNode? origen;
        try
        {
            origen = JsonNode.Parse(payloadInternoJson);
        }
        catch (JsonException ex)
        {
            return Result.Failure<string>(Error.Validation(
                "IntegrationHub.Mapping.PayloadInternoInvalido", $"El payload interno no es JSON válido: {ex.Message}"));
        }

        var destino = new JsonObject();

        foreach (var mapping in mappings)
        {
            var valor = GetByPath(origen, mapping.CampoOrigen);
            if (valor is null)
            {
                continue;
            }

            SetByPath(destino, mapping.CampoDestino, valor.DeepClone());
        }

        return destino.ToJsonString();
    }

    private static JsonNode? GetByPath(JsonNode? raiz, string path)
    {
        var segmentos = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        JsonNode? actual = raiz;

        foreach (var segmento in segmentos)
        {
            if (actual is not JsonObject objeto || !objeto.TryGetPropertyValue(segmento, out var siguiente))
            {
                return null;
            }

            actual = siguiente;
        }

        return actual;
    }

    private static void SetByPath(JsonObject raiz, string path, JsonNode? valor)
    {
        var segmentos = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var actual = raiz;

        for (var i = 0; i < segmentos.Length - 1; i++)
        {
            if (actual[segmentos[i]] is not JsonObject siguiente)
            {
                siguiente = new JsonObject();
                actual[segmentos[i]] = siguiente;
            }

            actual = siguiente;
        }

        actual[segmentos[^1]] = valor;
    }
}
