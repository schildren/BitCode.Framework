using System.Security.Cryptography;
using System.Text;

namespace BitCode.Framework.Platform.FeatureManagement.Evaluacion;

/// <summary>
/// Algoritmo de bucketing determinístico para segmentos <see cref="Segmentos.SegmentoTipo.PorPorcentaje"/>
/// (Fase 6, módulo Feature Management): dado un flag y un identificador de contexto (típicamente el
/// usuario, o el tenant si no hay usuario autenticado), calcula un número estable en <c>[0, 100)</c> --
/// el MISMO contexto siempre cae en el MISMO bucket para el MISMO flag (no es aleatorio en cada
/// evaluación, condición necesaria para que un rollout "gradual" no le muestre/oculte la funcionalidad a
/// un mismo usuario en requests sucesivos). Distintos flags dan buckets distintos para el mismo contexto
/// (se incluye el <c>featureFlagId</c> en el material hasheado) -- así que un usuario en el bucket 5 de
/// un flag no está necesariamente en el bucket 5 de otro.
///
/// Se usa SHA-256 (no un hash no criptográfico como <see cref="string.GetHashCode"/>, que además NO es
/// estable entre ejecuciones del proceso por diseño -- ver la documentación de .NET sobre
/// <c>GetHashCode</c> y aleatorización de hash de string) para tener una distribución uniforme conocida
/// y determinismo garantizado entre procesos/versiones de runtime.
/// </summary>
internal static class PorcentajeRolloutHasher
{
    /// <summary>Devuelve <see langword="true"/> si el bucket determinístico de
    /// (<paramref name="featureFlagId"/>, <paramref name="contextId"/>) cae dentro del
    /// <paramref name="porcentaje"/> configurado (0-100).</summary>
    public static bool PerteneceAlPorcentaje(Guid featureFlagId, string contextId, int porcentaje)
    {
        if (porcentaje <= 0)
        {
            return false;
        }

        if (porcentaje >= 100)
        {
            return true;
        }

        return Bucket(featureFlagId, contextId) < porcentaje;
    }

    /// <summary>Bucket determinístico en <c>[0, 100)</c> -- expuesto <c>internal</c> (no
    /// <see langword="private"/>) para poder probarlo directamente con una distribución de muchos
    /// contextos sin pasar por el flujo HTTP completo.</summary>
    internal static int Bucket(Guid featureFlagId, string contextId)
    {
        var material = Encoding.UTF8.GetBytes($"{featureFlagId:N}:{contextId}");
        var hash = SHA256.HashData(material);

        // Los primeros 4 bytes del hash como entero sin signo, módulo 100 -- suficiente entropía para una
        // distribución uniforme sobre 100 buckets sin necesitar el hash completo de 32 bytes.
        var value = BitConverter.ToUInt32(hash, 0);
        return (int)(value % 100);
    }
}
