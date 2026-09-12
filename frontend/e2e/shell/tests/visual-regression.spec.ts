import { expect, test } from '@playwright/test';

/**
 * F7-15 ("visual regression"): captura de pantalla determinística por página clave, comparada contra un
 * baseline versionado (`*-snapshots/`, generado con `npx playwright test --update-snapshots` y commiteado
 * al repo). Usa el mecanismo nativo de Playwright (`toHaveScreenshot`) -- sin agregar una herramienta de
 * visual regression externa (Percy/Chromatic/etc.), suficiente para el alcance actual (pocas páginas,
 * sin necesidad de revisión colaborativa de diffs en la nube todavía).
 *
 * Determinismo: `animations: 'disabled'` evita falsos positivos por transiciones CSS en vuelo; el viewport
 * fijo de `playwright.config.ts` (`devices['Desktop Chrome']`) evita diffs por tamaño de ventana variable.
 */

test('captura visual de la página de inicio', async ({ page }) => {
  await page.goto('/inicio');
  await expect(page).toHaveScreenshot('inicio.png', { animations: 'disabled' });
});

test('captura visual de la página de Documentos', async ({ page }) => {
  await page.goto('/procesos/documentos');
  await expect(page).toHaveScreenshot('documentos.png', { animations: 'disabled' });
});
