using BitCode.Framework.Platform.Notifications.Plantillas;
using BitCode.Framework.Shared.Domain.Specifications;

namespace BitCode.Framework.Platform.Notifications.Preferencias;

/// <summary>Existencia exacta de un opt-out -- la consulta que <c>NotificationSender</c> hace ANTES de
/// intentar entregar una notificación (ver <c>docs/guia-notifications.md</c>, sección
/// "Preferencias").</summary>
internal sealed class PreferenciaExactaSpecification : Specification<UserNotificationPreference>
{
    public PreferenciaExactaSpecification(Guid userId, string codigoPlantilla, NotificationChannel canal) =>
        ApplyCriteria(p => p.UserId == userId && p.CodigoPlantilla == codigoPlantilla && p.Canal == canal);
}

/// <summary>Todas las preferencias (opt-outs) del actor autenticado -- SIEMPRE acotada al propio actor,
/// nunca recibe el <c>UserId</c> de otro usuario como parámetro (mismo criterio de seguridad que
/// <c>BandejaDeActorSpecification</c> de Task Inbox).</summary>
internal sealed class PreferenciasDeActorSpecification : Specification<UserNotificationPreference>
{
    public PreferenciasDeActorSpecification(Guid userId) => ApplyCriteria(p => p.UserId == userId);
}
