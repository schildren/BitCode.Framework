using System.Globalization;

namespace BitCode.Framework.Platform.Workflow.Definiciones;

/// <summary>
/// Evalúa la expresión simple de una <see cref="WorkflowTransition.ReglaExpresion"/> contra el
/// diccionario de variables de contexto de una <see cref="Instancias.WorkflowInstance"/> --
/// DELIBERADAMENTE no es un motor de reglas genérico tipo Drools (Plan Maestro, "Épica de Workflow"):
/// solo entiende "{variable} {operador} {valor}" con operadores de comparación simples. Un consumidor
/// que necesite una condición más rica que esto debe modelarla como más de un <see cref="WorkflowState"/>
/// intermedio con transiciones más simples, o delegar en su propio código (fuera de este framework) antes
/// de llamar a <c>IniciarInstanciaCommand</c>/<c>ResolverTareaCommand</c> con las variables ya resueltas.
/// </summary>
public static class WorkflowRuleEvaluator
{
    private static readonly string[] Operadores = [">=", "<=", "!=", "==", ">", "<"];

    /// <summary>
    /// <see langword="true"/> si <paramref name="expresion"/> es nula/vacía (regla ausente = siempre
    /// aplica) o si evalúa a verdadero contra <paramref name="variables"/>. Una expresión malformada, un
    /// nombre de variable ausente del diccionario, o un operador no soportado se tratan como "no aplica"
    /// (<see langword="false"/>) en vez de lanzar una excepción -- una transición cuya regla no se puede
    /// evaluar simplemente no está disponible, no rompe el avance de la instancia.
    /// </summary>
    public static bool Evaluar(string? expresion, IReadOnlyDictionary<string, string> variables)
    {
        if (string.IsNullOrWhiteSpace(expresion))
        {
            return true;
        }

        foreach (var operador in Operadores)
        {
            var index = expresion.IndexOf(operador, StringComparison.Ordinal);
            if (index <= 0)
            {
                continue;
            }

            var variable = expresion[..index].Trim();
            var valorEsperado = expresion[(index + operador.Length)..].Trim();

            if (!variables.TryGetValue(variable, out var valorActual))
            {
                return false;
            }

            return Comparar(valorActual, operador, valorEsperado);
        }

        return false;
    }

    private static bool Comparar(string valorActual, string operador, string valorEsperado)
    {
        if (decimal.TryParse(valorActual, NumberStyles.Number, CultureInfo.InvariantCulture, out var actualNumerico) &&
            decimal.TryParse(valorEsperado, NumberStyles.Number, CultureInfo.InvariantCulture, out var esperadoNumerico))
        {
            return operador switch
            {
                "==" => actualNumerico == esperadoNumerico,
                "!=" => actualNumerico != esperadoNumerico,
                ">" => actualNumerico > esperadoNumerico,
                ">=" => actualNumerico >= esperadoNumerico,
                "<" => actualNumerico < esperadoNumerico,
                "<=" => actualNumerico <= esperadoNumerico,
                _ => false,
            };
        }

        return operador switch
        {
            "==" => string.Equals(valorActual, valorEsperado, StringComparison.Ordinal),
            "!=" => !string.Equals(valorActual, valorEsperado, StringComparison.Ordinal),
            _ => false,
        };
    }
}
