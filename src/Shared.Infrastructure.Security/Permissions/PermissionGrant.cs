namespace BitCode.Framework.Shared.Infrastructure.Security.Permissions;

/// <summary>
/// Un permiso efectivo concedido a la identidad actual, junto con el origen que lo otorgó (F2-07,
/// RBAC 2.0) — la pieza que hace "trazable" el resultado de <see cref="IPermissionEvaluator"/> (el
/// criterio de aceptación literal de la tarea): ante una pregunta de auditoría ("¿por qué este sujeto
/// puede ejercer este permiso?") alcanza con inspeccionar <see cref="Source"/> en vez de reconstruir
/// manualmente qué rol o claim del token lo originó. Ver <see cref="PermissionGrantSources"/> para los
/// valores posibles de <see cref="Source"/>.
/// </summary>
public sealed record PermissionGrant(string Permission, string Source);
