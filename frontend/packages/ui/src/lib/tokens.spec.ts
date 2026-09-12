import { readFileSync } from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';
// @ts-expect-error -- build-tokens.mjs es JS plano sin tipos, vive fuera de src/ (no es parte del
// paquete publicado, solo de las herramientas de build/test de este proyecto).
import { generateAll } from '../../tokens/build-tokens.mjs';
import { tokens } from './tokens.generated';

const uiRoot = path.resolve(__dirname, '..', '..');
const tokensDir = path.join(uiRoot, 'tokens');
const stylesDir = path.join(uiRoot, 'src', 'styles');

describe('design tokens (F7-02)', () => {
  it('el CSS/SCSS/TS versionados coinciden byte a byte con lo generado desde las fuentes JSON', () => {
    // Este test es la garantía de que nadie edita tokens.css/tokens.scss/tokens.generated.ts "a
    // mano" sin regenerar desde primitives.tokens.json / semantic.tokens.json: si las fuentes
    // cambian y no se corre `node tokens/build-tokens.mjs`, este test falla.
    const fresh = generateAll(tokensDir);

    const committedCss = readFileSync(path.join(stylesDir, 'tokens.css'), 'utf8');
    const committedScss = readFileSync(path.join(stylesDir, 'tokens.scss'), 'utf8');
    const committedTs = readFileSync(path.join(uiRoot, 'src', 'lib', 'tokens.generated.ts'), 'utf8');

    expect(committedCss).toBe(fresh.css);
    expect(committedScss).toBe(fresh.scss);
    expect(committedTs).toBe(fresh.ts);
  });

  it('expone en :root las custom properties de espaciado, tipografía y radios esperadas', () => {
    const css = readFileSync(path.join(stylesDir, 'tokens.css'), 'utf8');

    for (const expected of [
      '--bc-space-4: 16px;',
      '--bc-font-family-sans:',
      '--bc-font-size-base: 16px;',
      '--bc-font-weight-semibold: 600;',
      '--bc-radius-md: 8px;',
      '--bc-shadow-md:',
      '--bc-duration-fast: 120ms;',
      '--bc-opacity-disabled: 0.4;',
    ]) {
      expect(css).toContain(expected);
    }
  });

  it('define los tokens de color semánticos de estado (success/warning/error/info) para tema claro y oscuro', () => {
    const css = readFileSync(path.join(stylesDir, 'tokens.css'), 'utf8');
    const [lightBlock, darkBlock] = css.split("[data-theme='dark']");

    for (const state of ['success', 'warning', 'error', 'info']) {
      for (const suffix of ['', '-bg', '-border']) {
        const varName = `--bc-color-${state}${suffix}:`;
        expect(lightBlock, `falta ${varName} en tema claro`).toContain(varName);
        expect(darkBlock, `falta ${varName} en tema oscuro`).toContain(varName);
      }
    }
  });

  it('define tokens de estado de interacción (hover/active/focus/disabled)', () => {
    const css = readFileSync(path.join(stylesDir, 'tokens.css'), 'utf8');

    expect(css).toContain('--bc-color-brand-primary-hover:');
    expect(css).toContain('--bc-color-brand-primary-active:');
    expect(css).toContain('--bc-color-focus-ring:');
    expect(css).toContain('--bc-color-text-disabled:');
    expect(css).toContain('--bc-opacity-hover:');
    expect(css).toContain('--bc-opacity-pressed:');
    expect(css).toContain('--bc-opacity-disabled:');
  });

  it('el mapa TypeScript exportado referencia custom properties, no valores crudos', () => {
    expect(tokens.color.brandPrimary).toBe('var(--bc-color-brand-primary)');
    expect(tokens.space['4']).toBe('var(--bc-space-4)');
    expect(tokens.fontSize.base).toBe('var(--bc-font-size-base)');
    expect(tokens.color.error).toBe('var(--bc-color-error)');
  });
});
