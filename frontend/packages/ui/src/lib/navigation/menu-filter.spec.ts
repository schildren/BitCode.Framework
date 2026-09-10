import { BitcodeUserClaims } from '@bitcode/auth';
import { describe, expect, it } from 'vitest';
import { filterMenuByPermissions } from './menu-filter';
import { BitcodeMenuItem } from './menu-item.model';

function claimsWith(permissions: readonly string[]): BitcodeUserClaims {
  return {
    subject: 'user-1',
    roles: [],
    permissions,
    raw: {},
  };
}

describe('filterMenuByPermissions (F7-05)', () => {
  it('sin claims (sesión no resuelta), sólo muestra items sin permiso requerido', () => {
    const items: BitcodeMenuItem[] = [
      { id: 'inicio', label: 'Inicio', link: '/inicio' },
      { id: 'usuarios', label: 'Usuarios', link: '/usuarios', requiredPermissions: 'identidad.usuarios.ver' },
    ];

    const visible = filterMenuByPermissions(items, null);

    expect(visible.map((item) => item.id)).toEqual(['inicio']);
  });

  it('muestra un item cuando el actor tiene el permiso requerido', () => {
    const items: BitcodeMenuItem[] = [
      { id: 'usuarios', label: 'Usuarios', link: '/usuarios', requiredPermissions: 'identidad.usuarios.ver' },
    ];

    const visible = filterMenuByPermissions(items, claimsWith(['identidad.usuarios.ver']));

    expect(visible.map((item) => item.id)).toEqual(['usuarios']);
  });

  it('oculta un item cuando al actor le falta el permiso requerido', () => {
    const items: BitcodeMenuItem[] = [
      { id: 'usuarios', label: 'Usuarios', link: '/usuarios', requiredPermissions: 'identidad.usuarios.ver' },
    ];

    const visible = filterMenuByPermissions(items, claimsWith(['otro.permiso']));

    expect(visible).toEqual([]);
  });

  it('respeta permissionMode "any" para múltiples permisos requeridos', () => {
    const items: BitcodeMenuItem[] = [
      {
        id: 'reportes',
        label: 'Reportes',
        link: '/reportes',
        requiredPermissions: ['reporting.reportes.ver', 'dashboard.tableros.ver'],
        permissionMode: 'any',
      },
    ];

    const visible = filterMenuByPermissions(items, claimsWith(['dashboard.tableros.ver']));

    expect(visible.map((item) => item.id)).toEqual(['reportes']);
  });

  it('un grupo sin link propio cuyos hijos quedan todos filtrados no se muestra (no queda vacío)', () => {
    const items: BitcodeMenuItem[] = [
      {
        id: 'administracion',
        label: 'Administración',
        children: [
          { id: 'usuarios', label: 'Usuarios', link: '/usuarios', requiredPermissions: 'identidad.usuarios.ver' },
          { id: 'roles', label: 'Roles', link: '/roles', requiredPermissions: 'identidad.roles.ver' },
        ],
      },
    ];

    const visible = filterMenuByPermissions(items, claimsWith([]));

    expect(visible).toEqual([]);
  });

  it('un grupo se muestra con sólo los hijos permitidos cuando al menos uno queda visible', () => {
    const items: BitcodeMenuItem[] = [
      {
        id: 'administracion',
        label: 'Administración',
        children: [
          { id: 'usuarios', label: 'Usuarios', link: '/usuarios', requiredPermissions: 'identidad.usuarios.ver' },
          { id: 'roles', label: 'Roles', link: '/roles', requiredPermissions: 'identidad.roles.ver' },
        ],
      },
    ];

    const visible = filterMenuByPermissions(items, claimsWith(['identidad.usuarios.ver']));

    expect(visible).toHaveLength(1);
    expect(visible[0].children?.map((child) => child.id)).toEqual(['usuarios']);
  });

  it('un grupo CON link propio se muestra igual aunque todos sus hijos queden filtrados', () => {
    const items: BitcodeMenuItem[] = [
      {
        id: 'documentos',
        label: 'Documentos',
        link: '/documentos',
        children: [
          { id: 'plantillas', label: 'Plantillas', link: '/documentos/plantillas', requiredPermissions: 'documents.plantillas.ver' },
        ],
      },
    ];

    const visible = filterMenuByPermissions(items, claimsWith([]));

    expect(visible.map((item) => item.id)).toEqual(['documentos']);
    expect(visible[0].children).toEqual([]);
  });

  it('un item cuyo padre no tiene permiso no se muestra aunque el hijo sí tendría permiso (se corta el árbol completo)', () => {
    const items: BitcodeMenuItem[] = [
      {
        id: 'administracion',
        label: 'Administración',
        requiredPermissions: 'identidad.administrar',
        children: [{ id: 'usuarios', label: 'Usuarios', link: '/usuarios' }],
      },
    ];

    const visible = filterMenuByPermissions(items, claimsWith([]));

    expect(visible).toEqual([]);
  });

  it('ordena por "order" ascendente, y deja al final (en orden estable) los items sin "order"', () => {
    const items: BitcodeMenuItem[] = [
      { id: 'c', label: 'C', link: '/c' },
      { id: 'a', label: 'A', link: '/a', order: 1 },
      { id: 'b', label: 'B', link: '/b', order: 0 },
      { id: 'd', label: 'D', link: '/d' },
    ];

    const visible = filterMenuByPermissions(items, null);

    expect(visible.map((item) => item.id)).toEqual(['b', 'a', 'c', 'd']);
  });

  it('es una función pura: no muta el array/objetos de entrada', () => {
    const items: BitcodeMenuItem[] = [
      {
        id: 'administracion',
        label: 'Administración',
        children: [{ id: 'usuarios', label: 'Usuarios', link: '/usuarios', requiredPermissions: 'identidad.usuarios.ver' }],
      },
    ];
    const snapshot = JSON.stringify(items);

    filterMenuByPermissions(items, claimsWith([]));

    expect(JSON.stringify(items)).toBe(snapshot);
  });
});
