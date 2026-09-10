import { describe, expect, it } from 'vitest';
import { criticalA11yViolations, runBitcodeA11yCheck } from './a11y-harness';

describe('runBitcodeA11yCheck (F7-12)', () => {
  it('no reporta hallazgos críticos para un formulario accesible (label asociado)', async () => {
    document.body.innerHTML = `
      <form>
        <label for="nombre">Nombre</label>
        <input id="nombre" type="text" />
      </form>
    `;

    const results = await runBitcodeA11yCheck(document.body);

    expect(criticalA11yViolations(results)).toEqual([]);
  });

  it('detecta un input sin label asociado como hallazgo crítico', async () => {
    document.body.innerHTML = `
      <form>
        <input id="sin-label" type="text" />
      </form>
    `;

    const results = await runBitcodeA11yCheck(document.body);

    const critical = criticalA11yViolations(results);
    expect(critical.length).toBeGreaterThan(0);
    expect(critical.some((violation) => violation.id === 'label')).toBe(true);
  });

  it('desactiva color-contrast (no confiable sin layout real en jsdom)', async () => {
    document.body.innerHTML = `<p style="color: #eee; background-color: #fff;">Texto casi invisible</p>`;

    const results = await runBitcodeA11yCheck(document.body);

    expect(results.violations.some((violation) => violation.id === 'color-contrast')).toBe(false);
  });
});
