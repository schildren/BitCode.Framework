import { BitcodeUserClaims, hasRequiredPermissions } from '@bitcode/auth';
import { BitcodeMenuItem } from './menu-item.model';

/**
 * Filtra un árbol de items de menú según los permisos del actor (F7-05), reutilizando EXACTAMENTE el
 * mismo mecanismo de chequeo que el resto de la autorización UI (`hasRequiredPermissions`, F7-04) -- no
 * reimplementa ninguna lógica de comparación de permisos propia.
 *
 * Reglas:
 * - Un item con `requiredPermissions` que el actor no satisface se excluye por completo (junto con sus
 *   `children`, si los tuviera -- no tiene sentido mostrar sub-items de un item padre no autorizado).
 * - Un item sin `requiredPermissions` es visible en sí mismo, pero si es un grupo puro (sin `link` propio)
 *   cuyos `children` quedan TODOS filtrados, tampoco se muestra (evita grupos vacíos en la navegación).
 * - El resultado se ordena por `order` (ascendente, los items sin `order` van al final en orden estable).
 *
 * Es una función PURA: mismo input siempre produce el mismo árbol resultante (mismo contenido, aunque
 * nueva identidad de array/objeto en cada llamada) -- de eso depende que `BitcodeMenuService` pueda
 * derivarla con `computed()` de forma predecible.
 */
export function filterMenuByPermissions(
  items: readonly BitcodeMenuItem[],
  claims: BitcodeUserClaims | null,
): BitcodeMenuItem[] {
  return sortByOrder(
    items
      .map((item) => filterMenuItem(item, claims))
      .filter((item): item is BitcodeMenuItem => item !== null),
  );
}

function filterMenuItem(item: BitcodeMenuItem, claims: BitcodeUserClaims | null): BitcodeMenuItem | null {
  if (
    item.requiredPermissions !== undefined &&
    !hasRequiredPermissions(claims, item.requiredPermissions, item.permissionMode ?? 'all')
  ) {
    return null;
  }

  if (!item.children || item.children.length === 0) {
    return item;
  }

  const visibleChildren = filterMenuByPermissions(item.children, claims);
  if (visibleChildren.length === 0 && !item.link) {
    // Grupo puro (sin página propia) sin ningún hijo visible: no tiene sentido mostrarlo vacío.
    return null;
  }

  return { ...item, children: visibleChildren };
}

function sortByOrder(items: readonly BitcodeMenuItem[]): BitcodeMenuItem[] {
  return items
    .map((item, index) => ({ item, index }))
    .sort((a, b) => {
      const orderA = a.item.order ?? Number.MAX_SAFE_INTEGER;
      const orderB = b.item.order ?? Number.MAX_SAFE_INTEGER;
      return orderA !== orderB ? orderA - orderB : a.index - b.index;
    })
    .map(({ item }) => item);
}
