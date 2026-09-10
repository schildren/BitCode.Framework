import { BitcodePermissionMode } from '@bitcode/auth';

/**
 * Item de navegación de un menú dinámico empresarial (F7-05, "Navigation shell").
 *
 * Modelo GENÉRICO: este paquete (`@bitcode/ui`) no conoce los módulos concretos de BitCode (Identity
 * Administration, Workflow, Documents, etc.) -- eso es responsabilidad de la app consumidora (ver
 * `apps/shell/src/app/navigation/shell-menu.config.ts`, que sí sabe qué módulos existen). Lo único que
 * este modelo asume es la MISMA convención de permisos `"{entidad}.{accion}"` que ya usa el resto de
 * `@bitcode/auth` (F7-04, `RequirePermissionAttribute` del lado backend).
 *
 * Se modela como árbol (`children` anidados), no como lista plana con `parentId`: un menú lateral
 * empresarial rara vez necesita más de 2-3 niveles de profundidad, y un árbol evita tener que
 * reconstruirlo a partir de referencias sueltas en el componente de navegación.
 */
export interface BitcodeMenuItem {
  /** Identificador único y estable del item (no necesariamente visible), usado por ejemplo para
   * recordar qué grupos están expandidos/colapsados en el componente de navegación. */
  readonly id: string;
  readonly label: string;
  /** Ruta de navegación de la SPA (`routerLink`). Ausente en items que son puramente un grupo/contenedor
   * de `children` (p. ej. "Administración" agrupando varios módulos) sin una página propia. */
  readonly link?: string;
  /** Nombre de ícono, sin acoplarse a ninguna librería de íconos concreta (F7-05 no incluye una -- ver
   * limitaciones documentadas en `docs/guia-frontend-navigation.md`). Puramente informativo/opcional. */
  readonly icon?: string;
  /**
   * Orden relativo entre hermanos (ascendente). Items sin `order` se ordenan al final, en el orden en que
   * aparecen en el array de origen (orden estable).
   */
  readonly order?: number;
  /**
   * Permiso(s) requeridos para que el item (y, transitivamente, sus `children`) sea visible. Sin este
   * campo, el item es visible para cualquier sesión (incluida una sesión aún no resuelta) -- igual que el
   * resto de los mecanismos de autorización UI de F7-04, "sin permiso configurado" significa "no
   * restringido", nunca "denegado por defecto por falta de dato".
   */
  readonly requiredPermissions?: string | readonly string[];
  /** Semántica cuando `requiredPermissions` tiene más de un permiso (ver `BitcodePermissionMode` de
   * `@bitcode/auth`). Default `'all'`, igual que `hasRequiredPermissions`. */
  readonly permissionMode?: BitcodePermissionMode;
  /** Sub-items anidados (grupo). Un grupo cuyos `children` quedan todos filtrados por permisos, y que no
   * tiene `link` propio, no debe aparecer en el árbol resuelto (ver `filterMenuByPermissions`). */
  readonly children?: readonly BitcodeMenuItem[];
}
