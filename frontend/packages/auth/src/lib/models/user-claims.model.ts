/**
 * Claims mínimos del usuario autenticado, expuestos de forma tipada para que otros paquetes
 * (`@bitcode/ui`, futuros guards de autorización de F7-04) los consuman sin volver a decodificar
 * ningún token del lado cliente.
 *
 * El esquema sigue el mismo criterio de nombres que usa el framework del lado backend para JWT propio
 * (`JwtTokenGenerator.cs`: `sub`, `unique_name`, `email`, `ClaimTypes.Role`, `TenantClaimTypes.TenantId`)
 * -- pero para la sesión BFF/OIDC (F2-02/F2-03) los claims reales provienen del `id_token` que emite el
 * IdP externo, cuyo esquema exacto depende de cómo esté configurado ese IdP (Keycloak u otro). Por eso
 * `raw` conserva todos los claims devueltos por el backend sin filtrar, y los campos tipados de arriba
 * son un mapeo de "mejor esfuerzo" sobre los nombres más comunes -- documentado explícitamente como una
 * simplificación honesta, no una garantía de contrato cerrado con el backend (ver limitaciones de F7-03).
 */
export interface BitcodeUserClaims {
  /** Claim `sub` (identificador único y estable del sujeto ante el IdP). */
  readonly subject: string;
  readonly userName?: string;
  readonly email?: string;
  /** Roles asignados al usuario (claim `role`/`roles`, según lo publique el IdP). */
  readonly roles: readonly string[];
  /** Tenant del usuario, si el proyecto es multi-tenant (F1-12). */
  readonly tenantId?: string;
  /** Todos los claims devueltos por el backend, sin normalizar -- vía de escape para necesidades que
   * los campos tipados de arriba no cubran, sin tener que ampliar este contrato cada vez. */
  readonly raw: Readonly<Record<string, unknown>>;
}
