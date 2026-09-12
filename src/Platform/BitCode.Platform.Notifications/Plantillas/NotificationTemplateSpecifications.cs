using BitCode.Framework.Shared.Domain.Specifications;

namespace BitCode.Framework.Platform.Notifications.Plantillas;

/// <summary>Búsqueda exacta por la clave lógica de una plantilla (código + canal + locale) -- usada
/// tanto para el chequeo de duplicados de <c>CrearNotificationTemplateCommand</c> como para la
/// resolución real que hace <c>NotificationSender</c> al disparar una notificación.</summary>
internal sealed class NotificationTemplateBusquedaSpecification : Specification<NotificationTemplate>
{
    public NotificationTemplateBusquedaSpecification(string codigo, NotificationChannel canal, string locale) =>
        ApplyCriteria(t => t.Codigo == codigo && t.Canal == canal && t.Locale == locale);
}

/// <summary>Misma búsqueda que <see cref="NotificationTemplateBusquedaSpecification"/> pero acotada a
/// plantillas activas -- la que realmente usa <c>NotificationSender</c> para resolver qué plantilla
/// renderizar (una plantilla desactivada nunca se resuelve, ver <see cref="NotificationTemplate.Activa"/>).</summary>
internal sealed class NotificationTemplateActivaSpecification : Specification<NotificationTemplate>
{
    public NotificationTemplateActivaSpecification(string codigo, NotificationChannel canal, string locale) =>
        ApplyCriteria(t => t.Codigo == codigo && t.Canal == canal && t.Locale == locale && t.Activa);
}
