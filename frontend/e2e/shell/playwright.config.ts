import { defineConfig, devices } from '@playwright/test';

/**
 * F7-15 ("Testing... E2E y visual regression"): E2E real contra `apps/shell` servido de forma estática
 * (build de producción real, no `ng serve` con recarga en caliente -- más cerca de lo que un usuario final
 * ve). `webServer` construye y sirve automáticamente antes de correr los tests (y reutiliza el servidor si
 * ya está corriendo en un `nx run shell:serve-static` manual, útil en desarrollo local).
 */
export default defineConfig({
  testDir: './tests',
  fullyParallel: true,
  forbidOnly: !!process.env['CI'],
  retries: process.env['CI'] ? 2 : 0,
  reporter: [['list']],
  use: {
    baseURL: 'http://127.0.0.1:4300',
    trace: 'on-first-retry',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: {
    command: 'node scripts/serve-shell-static.mjs --port=4300',
    url: 'http://127.0.0.1:4300',
    reuseExistingServer: !process.env['CI'],
    cwd: '../..',
    timeout: 180_000,
  },
});
