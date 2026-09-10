import { describe, expect, it } from 'vitest';
import { BitcodeUserClaims } from '../models/user-claims.model';
import { getActorAttribute } from './actor-attributes';

const claims: BitcodeUserClaims = {
  subject: 'user-1',
  roles: ['Admin'],
  permissions: ['identidad.usuarios.crear'],
  tenantId: 'norte',
  raw: { sucursal: 'norte-centro', nivelAprobacionMinimo: 2 },
};

describe('getActorAttribute', () => {
  it('sin claims (no autenticado), devuelve undefined para cualquier atributo', () => {
    expect(getActorAttribute(null, 'tenantId')).toBeUndefined();
  });

  it('resuelve los campos tipados conocidos (tenantId, roles, permissions)', () => {
    expect(getActorAttribute(claims, 'tenantId')).toBe('norte');
    expect(getActorAttribute(claims, 'roles')).toEqual(['Admin']);
    expect(getActorAttribute(claims, 'permissions')).toEqual(['identidad.usuarios.crear']);
  });

  it('para cualquier otro atributo ABAC (sucursal, área, etc.), cae a "raw" sin tener que ampliar el modelo tipado', () => {
    expect(getActorAttribute(claims, 'sucursal')).toBe('norte-centro');
    expect(getActorAttribute(claims, 'nivelAprobacionMinimo')).toBe(2);
  });

  it('un atributo que no existe ni en los campos tipados ni en "raw" devuelve undefined', () => {
    expect(getActorAttribute(claims, 'inexistente')).toBeUndefined();
  });
});
