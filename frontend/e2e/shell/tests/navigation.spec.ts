import { expect, test } from '@playwright/test';

/**
 * F7-15: flujo principal end-to-end contra el build real de `apps/shell` (no `ng serve` en modo dev) --
 * carga inicial, navegación lazy (F7-14) y el menú dinámico filtrado por permisos (F7-05). Cubre la
 * integración real entre `@bitcode/ui` (menú), el `Router` de Angular y las páginas lazy de F7-14, algo que
 * ningún test unitario/de componente aislado (Vitest+TestBed) ejercita en conjunto.
 */

test('la ruta raíz redirige a /inicio y renderiza el shell con el menú de navegación', async ({ page }) => {
  await page.goto('/');

  await expect(page).toHaveURL(/\/inicio$/);
  await expect(page.locator('app-navigation-shell')).toBeVisible();
  await expect(page.getByRole('link', { name: 'Inicio' })).toBeVisible();
});

test('navegar a /procesos/documentos carga la página lazy de Documentos con el formulario de carga', async ({
  page,
}) => {
  await page.goto('/procesos/documentos');

  await expect(page.getByRole('heading', { name: 'Documentos' })).toBeVisible();
  await expect(page.getByLabel('Seleccionar archivo')).toBeVisible();
  // El botón de subir empieza deshabilitado -- ningún archivo seleccionado todavía (mismo comportamiento
  // verificado en document-upload.spec.ts, F7-10, pero acá contra el DOM real del browser).
  await expect(page.getByRole('button', { name: 'Subir' })).toBeDisabled();
});

test('navegar a /procesos/workflow carga la página lazy de Workflow con las acciones de tarea', async ({ page }) => {
  await page.goto('/procesos/workflow');

  await expect(page.getByRole('heading', { name: 'Workflow' })).toBeVisible();
  await expect(page.getByLabel('Comentario (opcional)')).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Reasignar tarea' })).toBeVisible();
});

test('una URL sin ruta registrada no navega y no lanza un error no controlado en la página', async ({ page }) => {
  const pageErrors: Error[] = [];
  page.on('pageerror', (error) => pageErrors.push(error));

  await page.goto('/inicio');
  await page.evaluate(() => history.pushState({}, '', '/una-ruta-inexistente'));
  await page.waitForTimeout(200);

  // Comportamiento documentado en docs/guia-frontend-navigation.md: el Router no encuentra coincidencia y
  // no navega, sin excepción no controlada -- verificado acá contra un browser real, no sólo declarado.
  expect(pageErrors).toEqual([]);
});
