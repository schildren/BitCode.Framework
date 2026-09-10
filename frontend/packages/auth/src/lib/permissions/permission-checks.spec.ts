import { describe, expect, it } from 'vitest';
import { BitcodeUserClaims } from '../models/user-claims.model';
import { hasRequiredPermissions } from './permission-checks';

function claimsWith(permissions: readonly string[]): BitcodeUserClaims {
  return {
    subject: 'user-1',
    roles: [],
    permissions,
    raw: {},
  };
}

describe('hasRequiredPermissions', () => {
  it('sin claims (no autenticado), deniega cualquier permiso requerido', () => {
    expect(hasRequiredPermissions(null, 'identidad.usuarios.crear')).toBe(false);
  });

  it('sin permisos requeridos (array vacío), siempre permite -- no hay nada que exigir', () => {
    expect(hasRequiredPermissions(null, [])).toBe(true);
    expect(hasRequiredPermissions(claimsWith([]), [])).toBe(true);
  });

  it('con el permiso exacto, permite (string único)', () => {
    expect(hasRequiredPermissions(claimsWith(['identidad.usuarios.crear']), 'identidad.usuarios.crear')).toBe(
      true,
    );
  });

  it('sin el permiso, deniega', () => {
    expect(hasRequiredPermissions(claimsWith(['identidad.usuarios.ver']), 'identidad.usuarios.crear')).toBe(
      false,
    );
  });

  describe('modo "all" (default) -- semántica AND', () => {
    it('con TODOS los permisos requeridos, permite', () => {
      const claims = claimsWith(['identidad.usuarios.ver', 'identidad.usuarios.crear']);
      expect(hasRequiredPermissions(claims, ['identidad.usuarios.ver', 'identidad.usuarios.crear'])).toBe(true);
    });

    it('si falta AL MENOS UNO de los permisos requeridos, deniega', () => {
      const claims = claimsWith(['identidad.usuarios.ver']);
      expect(hasRequiredPermissions(claims, ['identidad.usuarios.ver', 'identidad.usuarios.crear'])).toBe(false);
    });
  });

  describe('modo "any" -- semántica OR', () => {
    it('con AL MENOS UNO de los permisos requeridos, permite', () => {
      const claims = claimsWith(['identidad.usuarios.crear']);
      expect(
        hasRequiredPermissions(claims, ['identidad.usuarios.ver', 'identidad.usuarios.crear'], 'any'),
      ).toBe(true);
    });

    it('sin ninguno de los permisos requeridos, deniega', () => {
      const claims = claimsWith(['identidad.sesiones.ver']);
      expect(
        hasRequiredPermissions(claims, ['identidad.usuarios.ver', 'identidad.usuarios.crear'], 'any'),
      ).toBe(false);
    });
  });
});
