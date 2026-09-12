using System.Text;

namespace BitCode.Framework.Platform.Notifications.Plantillas;

/// <summary>
/// Reemplazo de placeholders <c>{variable}</c> DELIBERADAMENTE simple -- ver el <c>remarks</c> de
/// <see cref="NotificationTemplate"/> para por qué esta tarea no introduce un motor de templating
/// genérico. Un placeholder sin valor provisto en <paramref name="datos"/> se deja tal cual en el texto
/// resultante (nunca lanza ni produce una cadena vacía silenciosa) -- así un error de tipeo en el nombre
/// del placeholder o un dato faltante es visible en el resultado ("Hola {nombreQueNoExiste}") en vez de
/// desaparecer sin dejar rastro.
/// </summary>
public static class NotificationTemplateRenderer
{
    public static string Render(string plantilla, IReadOnlyDictionary<string, string> datos)
    {
        ArgumentNullException.ThrowIfNull(plantilla);
        ArgumentNullException.ThrowIfNull(datos);

        if (plantilla.Length == 0)
        {
            return plantilla;
        }

        var resultado = new StringBuilder(plantilla.Length);
        var indice = 0;

        while (indice < plantilla.Length)
        {
            var aperturaIndice = plantilla.IndexOf('{', indice);
            if (aperturaIndice < 0)
            {
                resultado.Append(plantilla, indice, plantilla.Length - indice);
                break;
            }

            var cierreIndice = plantilla.IndexOf('}', aperturaIndice + 1);
            if (cierreIndice < 0)
            {
                // '{' sin '}' de cierre correspondiente: se copia tal cual, no es un placeholder válido.
                resultado.Append(plantilla, indice, plantilla.Length - indice);
                break;
            }

            resultado.Append(plantilla, indice, aperturaIndice - indice);

            var nombrePlaceholder = plantilla.Substring(aperturaIndice + 1, cierreIndice - aperturaIndice - 1);
            resultado.Append(
                datos.TryGetValue(nombrePlaceholder, out var valor)
                    ? valor
                    : plantilla.Substring(aperturaIndice, cierreIndice - aperturaIndice + 1));

            indice = cierreIndice + 1;
        }

        return resultado.ToString();
    }
}
