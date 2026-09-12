using BitCode.Framework.Shared.Kernel;

namespace BitCode.Framework.Shared.Application.Idempotency;

/// <summary>Errores propios de <c>IdempotencyBehavior</c> (F1-22).</summary>
public static class IdempotencyErrors
{
    /// <summary>
    /// Un comando <c>IIdempotentCommand</c> se ejecutó sin una Idempotency-Key (o vacía/blanco).
    /// Decisión F1-22: se rechaza en vez de dejarlo pasar como un comando no idempotente cualquiera
    /// — dejarlo pasar en silencio daría una falsa sensación de seguridad, ya que el propósito del
    /// marcador es justamente que el framework pueda detectar un reintento, algo que solo puede hacer
    /// si tiene una clave con la cual identificarlo.
    /// </summary>
    public static readonly Error KeyRequired = Error.Validation(
        "Idempotency.KeyRequired",
        "Este comando requiere el header Idempotency-Key para garantizar que un reintento no duplique la operación.");

    /// <summary>
    /// La Idempotency-Key ya tiene un resultado guardado, pero con un hash de request distinto: el
    /// cliente reutilizó la misma clave para una operación distinta, lo que nunca se ejecuta como si
    /// fuera el mismo reintento.
    /// </summary>
    public static readonly Error KeyReused = Error.Conflict(
        "Idempotency.KeyReused",
        "La Idempotency-Key ya se usó con un payload distinto; usá una clave nueva para una operación distinta.");
}
