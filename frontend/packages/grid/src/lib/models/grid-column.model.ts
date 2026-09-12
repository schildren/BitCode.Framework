import { BitcodePermissionMode } from '@bitcode/auth';

/**
 * Tipo semántico de una columna (F7-07). Puramente informativo hoy -- afecta la alineación por defecto
 * de la celda (`grid.scss`, `.bc-grid__cell--number` alinea a la derecha) pero NO aplica ningún formato
 * automático de fecha/moneda (eso es i18n real, F7-11): un `formatter` explícito es la única forma de
 * controlar el texto mostrado.
 */
export type BitcodeGridColumnType = 'text' | 'number' | 'date' | 'boolean' | 'custom';

/**
 * Configuración tipada de una columna de `<lib-bitcode-grid>` (F7-07), análoga a `BitcodeMenuItem` de
 * `@bitcode/ui` (F7-05): datos puros, sin acoplarse a ninguna entidad de negocio concreta -- el consumidor
 * (p. ej. un listado de usuarios) define su propio array de columnas para SU tipo `T`.
 */
export interface BitcodeGridColumn<T> {
  /** Identificador estable de la columna. Es el mismo valor que viaja en `BitcodeGridSort.columnId` y en
   * las claves de `BitcodeGridFilterState.columns` -- el data source lo recibe tal cual, nunca se infiere
   * mágicamente desde `header`. */
  readonly id: string;
  readonly header: string;
  /** Lee el valor crudo de la celda a partir de la fila. Se mantiene separado de `formatter` a propósito:
   * `accessor` es el valor "de dominio" (útil, por ejemplo, para un futuro export), `formatter` es
   * exclusivamente el texto a mostrar. */
  readonly accessor: (item: T) => unknown;
  /** Formatea el valor ya leído por `accessor` a texto de celda. Sin `formatter`, se usa `String(valor)`
   * (`''` para `null`/`undefined`). */
  readonly formatter?: (value: unknown, item: T) => string;
  readonly type?: BitcodeGridColumnType;
  /** Ancho CSS de la columna (p. ej. `'160px'`, `'2fr'`). Sin valor, la columna se reparte el espacio
   * restante en partes iguales (`'1fr'`) -- ver `grid.ts`, `gridTemplateColumns`. */
  readonly width?: string;
  /** Si `true`, el encabezado es clickeable y emite un cambio de `BitcodeGridSort` -- ORDENADO POR EL
   * DATA SOURCE (sección "Ordenamiento server-side" de F7-07), la grilla nunca reordena en el cliente. */
  readonly sortable?: boolean;
  /** Si `true`, se muestra un input de filtro por columna en la fila de filtros -- el valor tipeado viaja
   * al data source vía `BitcodeGridFilterState.columns[id]`, nunca se aplica con `Array.filter` local. */
  readonly filterable?: boolean;
  /** Permiso(s) requeridos para que la columna sea visible (mismo mecanismo que `BitcodeMenuItem`,
   * reutilizando `hasRequiredPermissions`/`@bitcode/auth` -- ver `grid.ts`). Sin este campo, la columna es
   * visible para cualquier sesión (incluida ninguna sesión / `@bitcode/auth` no provisto en absoluto). */
  readonly requiredPermissions?: string | readonly string[];
  readonly permissionMode?: BitcodePermissionMode;
}
