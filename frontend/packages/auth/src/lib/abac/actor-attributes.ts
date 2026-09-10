import { BitcodeUserClaims } from '../models/user-claims.model';

/**
 * ABAC del lado cliente (F7-04) -- alcance deliberadamente limitado, documentado honestamente.
 *
 * El backend (`AttributeScopeAbacRule`, `src/Shared.Infrastructure.Security/Abac/`) evalúa reglas de
 * alcance dinámicas que comparan un atributo del ACTOR (p. ej. el claim `tenantId`/`empresa`/`sucursal`)
 * contra un atributo del RECURSO concreto (`AbacResource.Attributes`, p. ej. la sucursal a la que
 * pertenece el pedido #123 que se está por editar). Replicar esa regla fielmente del lado cliente
 * requeriría que el cliente conociera el recurso concreto en cada punto de uso -- que es exactamente lo
 * que un guard de ruta genérico o una directiva reutilizable NO conocen de antemano (la ruta es
 * `/pedidos/:id`, no "el pedido de la sucursal Norte"). Por eso este primer corte de ABAC-cliente NO
 * intenta adivinar ni reimplementar `AttributeScopeAbacRule` -- eso sería una implementación que
 * aparenta funcionar pero no puede ser correcta con la información que el cliente tiene disponible en un
 * guard/directiva genérico.
 *
 * Lo que SÍ provee esta primera versión:
 * 1. `getActorAttribute` -- lee de forma tipada un atributo del ACTOR (tenant u otro claim de alcance
 *    publicado en la sesión), la mitad de la ecuación ABAC que el cliente sí conoce sin depender del
 *    recurso.
 * 2. `BitcodeIfActorDirective` (`abac/if-actor.directive.ts`) -- mecanismo GENÉRICO que recibe un
 *    PREDICADO (`(claims) => boolean`), no una regla hardcodeada. Un componente futuro que SÍ conoce el
 *    recurso concreto (porque ya lo cargó, p. ej. el pedido #123 completo) puede escribir su propia
 *    condición de alcance ahí mismo (`*bitcodeIfActor="pedido().sucursal === claims()?.tenantId"` o
 *    similar) sin que este paquete tenga que anticipar cada tipo de recurso posible.
 *
 * Mismo criterio de honestidad que el resto de F7-03/F7-04: esto NO es "ABAC completo en el cliente", es
 * la porción que es correcta implementar sin conocer el recurso, más una vía de escape genérica para el
 * resto -- y, como todo lo demás en este paquete, una conveniencia de UX: la regla real sigue
 * evaluándose server-side en cada request.
 */
export function getActorAttribute(claims: BitcodeUserClaims | null, key: string): unknown {
  if (!claims) {
    return undefined;
  }
  if (key === 'tenantId') {
    return claims.tenantId;
  }
  if (key === 'roles') {
    return claims.roles;
  }
  if (key === 'permissions') {
    return claims.permissions;
  }
  return claims.raw[key];
}
