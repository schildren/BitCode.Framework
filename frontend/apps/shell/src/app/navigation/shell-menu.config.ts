import { BitcodeMenuItem } from '@bitcode/ui';

/**
 * Configuración CONCRETA del menú de `apps/shell` (F7-05): a diferencia del modelo/servicio genérico de
 * `@bitcode/ui` (`BitcodeMenuItem`, `BitcodeMenuService`, que no conocen ningún módulo de negocio en
 * particular), este archivo sí sabe qué módulos existen -- los 12 módulos de plataforma implementados en
 * la Fase 6 del Plan Maestro (`docs/plan-maestro-bitcode-ia.md`).
 *
 * Es un árbol de EJEMPLO representativo de un menú lateral empresarial real, no un catálogo verificado
 * contra permisos reales emitidos por ningún backend: los `requiredPermissions` siguen la convención
 * `"{entidad}.{accion}"` documentada en `docs/guia-frontend-auth.md` (sección 5.1) y son razonables por
 * analogía con los módulos de Fase 6, pero NINGUNO de ellos fue verificado contra el catálogo real de
 * permisos que cada módulo backend expone (`IdentityAdministrationPermissions` y equivalentes por módulo)
 * -- eso requeriría inspeccionar los 12 módulos uno por uno, fuera del alcance de F7-05 (que es sobre el
 * MECANISMO de menú dinámico, no sobre cablear cada módulo real con su ruta/permiso definitivo). Cuando
 * cada módulo tenga su propia UI (F7-09 Workflow, F7-10 Documents, y las tareas futuras para el resto),
 * este archivo es el lugar natural para reemplazar el permiso de ejemplo por el real y apuntar `link` a
 * la ruta real de esa UI (hoy ninguna de estas rutas está registrada en `app.routes.ts` -- ver
 * `docs/guia-frontend-navigation.md`, sección de limitaciones).
 */
export const SHELL_MENU_ITEMS: readonly BitcodeMenuItem[] = [
  { id: 'inicio', label: 'Inicio', link: '/inicio', icon: 'home', order: 0 },
  {
    id: 'administracion',
    label: 'Administración',
    icon: 'admin_panel_settings',
    order: 10,
    children: [
      {
        id: 'identity-administration',
        label: 'Identidad y accesos',
        link: '/administracion/identidad',
        requiredPermissions: 'identidad.usuarios.ver',
      },
      {
        id: 'organization',
        label: 'Organización',
        link: '/administracion/organizacion',
        requiredPermissions: 'organizacion.estructura.ver',
      },
      {
        id: 'catalogs',
        label: 'Catálogos',
        link: '/administracion/catalogos',
        requiredPermissions: 'catalogos.items.ver',
      },
      {
        id: 'feature-management',
        label: 'Feature Management',
        link: '/administracion/features',
        requiredPermissions: 'features.flags.ver',
      },
    ],
  },
  {
    id: 'procesos',
    label: 'Contenido y procesos',
    icon: 'account_tree',
    order: 20,
    children: [
      {
        id: 'documents',
        label: 'Documentos',
        link: '/procesos/documentos',
        requiredPermissions: 'documentos.archivos.ver',
      },
      {
        id: 'workflow',
        label: 'Workflow',
        link: '/procesos/workflow',
        requiredPermissions: 'workflow.instancias.ver',
      },
      {
        id: 'task-inbox',
        label: 'Bandeja de tareas',
        link: '/procesos/tareas',
        requiredPermissions: 'tareas.bandeja.ver',
      },
      {
        id: 'notifications',
        label: 'Notificaciones',
        link: '/procesos/notificaciones',
        requiredPermissions: 'notificaciones.mensajes.ver',
      },
    ],
  },
  {
    id: 'integraciones',
    label: 'Integraciones',
    icon: 'sync_alt',
    order: 30,
    children: [
      {
        id: 'integration-hub',
        label: 'Integration Hub',
        link: '/integraciones/hub',
        requiredPermissions: 'integraciones.conectores.ver',
      },
      {
        id: 'import-export',
        label: 'Importación y exportación',
        link: '/integraciones/import-export',
        requiredPermissions: 'importexport.trabajos.ver',
      },
    ],
  },
  {
    id: 'analitica',
    label: 'Analítica',
    icon: 'insights',
    order: 40,
    children: [
      {
        id: 'reporting',
        label: 'Reportes',
        link: '/analitica/reportes',
        requiredPermissions: 'reporting.reportes.ver',
      },
      {
        id: 'dashboard',
        label: 'Tableros',
        link: '/analitica/tableros',
        requiredPermissions: 'dashboard.tableros.ver',
      },
    ],
  },
];
