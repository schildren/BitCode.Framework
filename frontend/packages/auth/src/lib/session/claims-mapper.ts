import { BitcodeUserClaims } from '../models/user-claims.model';

/**
 * Convierte el JSON crudo devuelto por `sessionEndpoint` (contrato propuesto por este paquete, ver
 * `docs/guia-frontend-auth.md`) al modelo tipado `BitcodeUserClaims`. Acepta varios alias comunes por
 * campo (`sub`/`subject`, `roles`/`role`, `tenantId`/`tenant_id`, `permissions`/`permission`) porque el
 * esquema exacto depende del IdP configurado y de cómo el backend decida serializar la sesión -- nunca
 * decodifica un JWT: recibe los claims ya resueltos como JSON plano.
 *
 * `permissions`/`permission` (F7-04) es, igual que el resto de este mapeo, un alias de "mejor esfuerzo":
 * ningún host BFF real publica hoy los permisos RBAC resueltos del usuario en `/bff/session` (ver
 * `docs/guia-frontend-auth.md` y el comentario de `BitcodeUserClaims.permissions`) -- si el campo no
 * viene en la respuesta, se resuelve como array vacío, nunca lanza.
 */
export function mapToUserClaims(raw: Record<string, unknown>): BitcodeUserClaims {
  const subject = firstString(raw, ['sub', 'subject']) ?? '';
  const userName = firstString(raw, ['userName', 'user_name', 'name', 'unique_name']);
  const email = firstString(raw, ['email']);
  const tenantId = firstString(raw, ['tenantId', 'tenant_id']);
  const roles = firstStringArray(raw, ['roles', 'role']);
  const permissions = firstStringArray(raw, ['permissions', 'permission']);

  return {
    subject,
    userName,
    email,
    tenantId,
    roles,
    permissions,
    raw,
  };
}

function firstString(raw: Record<string, unknown>, keys: readonly string[]): string | undefined {
  for (const key of keys) {
    const value = raw[key];
    if (typeof value === 'string' && value.length > 0) {
      return value;
    }
  }
  return undefined;
}

function firstStringArray(raw: Record<string, unknown>, keys: readonly string[]): readonly string[] {
  for (const key of keys) {
    const value = raw[key];
    if (Array.isArray(value)) {
      return value.filter((item): item is string => typeof item === 'string');
    }
    if (typeof value === 'string' && value.length > 0) {
      return [value];
    }
  }
  return [];
}
