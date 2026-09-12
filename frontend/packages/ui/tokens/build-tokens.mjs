#!/usr/bin/env node
/**
 * BitCode Design Tokens — generador (F7-02).
 *
 * Lee las fuentes de verdad (`primitives.tokens.json`, `semantic.tokens.json`, ambas en formato
 * inspirado en el Design Tokens Community Group: nodos hoja con `$value`/`$type`, alias con la
 * sintaxis `{grupo.clave}`) y genera artefactos consumibles:
 *
 *   - src/styles/tokens.css        Custom properties CSS (`--bc-*`), tema claro en `:root`,
 *                                  tema oscuro en `[data-theme="dark"]`.
 *   - src/styles/tokens.scss       Variables SCSS de conveniencia (`$bc-*`) que envuelven `var(--bc-*)`.
 *   - src/lib/tokens.generated.ts  Mapa TypeScript tipado de nombre de token -> `var(--bc-*)`, para
 *                                  uso programático (p. ej. canvas/charts que no pueden leer CSS).
 *
 * Este archivo expone funciones puras (para poder testear la generación sin tocar disco) y, al
 * ejecutarse directamente (`node tokens/build-tokens.mjs`), escribe los tres artefactos.
 *
 * IMPORTANTE: los tres artefactos de salida son generados. No editarlos a mano — cualquier cambio
 * de diseño se hace en los `*.tokens.json` de esta carpeta y se regenera con este script.
 */
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const __dirname = path.dirname(fileURLToPath(import.meta.url));

const ALIAS_RE = /^\{([\w.]+)\}$/;

/** Convierte `brandPrimaryHover` -> `brand-primary-hover`. Las claves numéricas quedan igual. */
export function kebabCase(key) {
  return key
    .replace(/([a-z0-9])([A-Z])/g, '$1-$2')
    .replace(/([A-Z])([A-Z][a-z])/g, '$1-$2')
    .toLowerCase();
}

/**
 * Aplana un árbol de tokens DTCG-like a un mapa `"grupo.subgrupo.clave" -> { value, type }`.
 * Un nodo es "hoja" si tiene la propiedad `$value`. Las claves que empiezan con `$` son metadata
 * (`$description`, etc.) y se ignoran como parte del path.
 */
export function flattenTokens(node, prefix = []) {
  const out = {};
  for (const [key, value] of Object.entries(node)) {
    if (key.startsWith('$')) continue;
    if (value && typeof value === 'object' && '$value' in value) {
      out[[...prefix, key].join('.')] = { value: value.$value, type: value.$type };
    } else if (value && typeof value === 'object') {
      Object.assign(out, flattenTokens(value, [...prefix, key]));
    }
  }
  return out;
}

/** Resuelve un alias `{grupo.clave}` contra el mapa de primitivos ya aplanado. Lanza si no existe. */
export function resolveAlias(rawValue, primitivesFlat) {
  const match = typeof rawValue === 'string' ? rawValue.match(ALIAS_RE) : null;
  if (!match) return rawValue;
  const target = primitivesFlat[match[1]];
  if (!target) {
    throw new Error(`Token semántico referencia un primitivo inexistente: ${rawValue}`);
  }
  return target.value;
}

/**
 * Carga y resuelve las dos fuentes de tokens. Devuelve:
 *   - primitives: mapa aplanado "grupo.clave" -> { value, type } (sin alias, valores crudos)
 *   - themes: { light: { color: { clave: valorResuelto } }, dark: { ... } }
 */
export function loadTokens({ primitivesPath, semanticPath }) {
  const primitivesJson = JSON.parse(readFileSync(primitivesPath, 'utf8'));
  const semanticJson = JSON.parse(readFileSync(semanticPath, 'utf8'));

  const primitives = flattenTokens(primitivesJson);

  const themes = {};
  for (const [themeName, themeNode] of Object.entries(semanticJson)) {
    if (themeName.startsWith('$')) continue;
    const flatSemantic = flattenTokens(themeNode);
    const resolved = {};
    for (const [tokenPath, { value }] of Object.entries(flatSemantic)) {
      resolved[tokenPath] = resolveAlias(value, primitives);
    }
    themes[themeName] = resolved;
  }

  return { primitives, themes };
}

/** Categorías de primitivos que NO son de color y se emiten directo en `:root` (sin theming). */
const NON_COLOR_CATEGORIES = [
  { prefix: 'spacing.', cssPrefix: 'space' },
  { prefix: 'fontFamily.', cssPrefix: 'font-family' },
  { prefix: 'fontSize.', cssPrefix: 'font-size' },
  { prefix: 'fontWeight.', cssPrefix: 'font-weight' },
  { prefix: 'lineHeight.', cssPrefix: 'line-height' },
  { prefix: 'radius.', cssPrefix: 'radius' },
  { prefix: 'shadow.', cssPrefix: 'shadow' },
  { prefix: 'duration.', cssPrefix: 'duration' },
  { prefix: 'easing.', cssPrefix: 'easing' },
  { prefix: 'opacity.', cssPrefix: 'opacity' },
];

function nonColorCssVars(primitives) {
  const vars = [];
  for (const { prefix, cssPrefix } of NON_COLOR_CATEGORIES) {
    for (const [tokenPath, { value }] of Object.entries(primitives)) {
      if (!tokenPath.startsWith(prefix)) continue;
      const key = tokenPath.slice(prefix.length);
      vars.push({ name: `--bc-${cssPrefix}-${kebabCase(key)}`, value });
    }
  }
  return vars;
}

function themeColorCssVars(themeColors) {
  return Object.entries(themeColors)
    .filter(([tokenPath]) => tokenPath.startsWith('color.'))
    .map(([tokenPath, value]) => ({
      name: `--bc-color-${kebabCase(tokenPath.slice('color.'.length))}`,
      value,
    }));
}

export function generateCss({ primitives, themes }) {
  const nonColor = nonColorCssVars(primitives);
  const light = themeColorCssVars(themes.light ?? {});
  const dark = themeColorCssVars(themes.dark ?? {});

  const lines = [];
  lines.push('/**');
  lines.push(' * BitCode Design Tokens — AUTO-GENERADO por tokens/build-tokens.mjs. No editar a mano.');
  lines.push(' * Fuente de verdad: packages/ui/tokens/primitives.tokens.json y semantic.tokens.json.');
  lines.push(' * Tema activo: `:root` (claro, por defecto) y `[data-theme="dark"]` (oscuro).');
  lines.push(' */');
  lines.push('');
  lines.push(':root {');
  for (const { name, value } of [...nonColor, ...light]) {
    lines.push(`  ${name}: ${value};`);
  }
  lines.push('}');
  lines.push('');
  lines.push('[data-theme=\'dark\'] {');
  for (const { name, value } of dark) {
    lines.push(`  ${name}: ${value};`);
  }
  lines.push('}');
  lines.push('');
  return lines.join('\n');
}

export function generateScss({ primitives, themes }) {
  const nonColor = nonColorCssVars(primitives);
  const light = themeColorCssVars(themes.light ?? {});

  const lines = [];
  lines.push('// BitCode Design Tokens — AUTO-GENERADO por tokens/build-tokens.mjs. No editar a mano.');
  lines.push('// Variables SCSS de conveniencia: envuelven las custom properties CSS (que son la fuente');
  lines.push('// de verdad en runtime, incluido el theming claro/oscuro). Útiles en archivos .scss de');
  lines.push('// componentes que prefieren `$bc-space-4` a `var(--bc-space-4)`.');
  lines.push('');
  for (const { name } of [...nonColor, ...light]) {
    const scssName = `$bc-${name.slice('--bc-'.length)}`;
    lines.push(`${scssName}: var(${name});`);
  }
  lines.push('');
  return lines.join('\n');
}

function toCamelSegments(tokenPath) {
  return tokenPath.split('.');
}

/** Arma un objeto anidado { space: { '4': 'var(--bc-space-4)' }, color: { surface: '...' } } */
function buildNestedVarMap(entries, rootKey) {
  const root = {};
  for (const { name, tokenKey } of entries) {
    const segments = toCamelSegments(tokenKey);
    let cursor = root;
    for (let i = 0; i < segments.length - 1; i++) {
      cursor[segments[i]] ??= {};
      cursor = cursor[segments[i]];
    }
    cursor[segments[segments.length - 1]] = `var(${name})`;
  }
  return { [rootKey]: root };
}

export function generateTs({ primitives, themes }) {
  const groups = {};

  for (const { prefix, cssPrefix } of NON_COLOR_CATEGORIES) {
    const entries = Object.keys(primitives)
      .filter((k) => k.startsWith(prefix))
      .map((k) => ({
        name: `--bc-${cssPrefix}-${kebabCase(k.slice(prefix.length))}`,
        tokenKey: k.slice(prefix.length),
      }));
    const rootKey = cssPrefix.replace(/-([a-z])/g, (_, c) => c.toUpperCase());
    Object.assign(groups, buildNestedVarMap(entries, rootKey));
  }

  const colorEntries = Object.keys(themes.light ?? {})
    .filter((k) => k.startsWith('color.'))
    .map((k) => ({
      name: `--bc-color-${kebabCase(k.slice('color.'.length))}`,
      tokenKey: k.slice('color.'.length),
    }));
  Object.assign(groups, buildNestedVarMap(colorEntries, 'color'));

  const lines = [];
  lines.push('/**');
  lines.push(' * BitCode Design Tokens — AUTO-GENERADO por tokens/build-tokens.mjs. No editar a mano.');
  lines.push(' *');
  lines.push(' * Mapa de tokens -> referencia a la custom property CSS correspondiente (`var(--bc-*)`),');
  lines.push(' * NO el valor resuelto: el valor real depende del tema activo (`:root` / `[data-theme=');
  lines.push(' * "dark"]`) en tiempo de ejecución, definido en styles/tokens.css. Usar este mapa desde');
  lines.push(' * TypeScript cuando se necesite referenciar un token de forma tipada (p. ej. al dibujar');
  lines.push(' * en un <canvas> o pasar un color a una librería de charts) sin hardcodear el nombre de');
  lines.push(' * la variable CSS a mano.');
  lines.push(' */');
  lines.push('');
  lines.push(`export const tokens = ${JSON.stringify(groups, null, 2)} as const;`);
  lines.push('');
  lines.push('export type BitCodeTokens = typeof tokens;');
  lines.push('');
  return lines.join('\n');
}

export function generateAll(tokensDir) {
  const data = loadTokens({
    primitivesPath: path.join(tokensDir, 'primitives.tokens.json'),
    semanticPath: path.join(tokensDir, 'semantic.tokens.json'),
  });
  return {
    css: generateCss(data),
    scss: generateScss(data),
    ts: generateTs(data),
  };
}

function main() {
  const tokensDir = __dirname;
  const uiRoot = path.join(tokensDir, '..');
  const stylesDir = path.join(uiRoot, 'src', 'styles');
  const libDir = path.join(uiRoot, 'src', 'lib');

  const { css, scss, ts } = generateAll(tokensDir);

  mkdirSync(stylesDir, { recursive: true });
  mkdirSync(libDir, { recursive: true });

  writeFileSync(path.join(stylesDir, 'tokens.css'), css, 'utf8');
  writeFileSync(path.join(stylesDir, 'tokens.scss'), scss, 'utf8');
  writeFileSync(path.join(libDir, 'tokens.generated.ts'), ts, 'utf8');

  console.log('[build-tokens] Generados: src/styles/tokens.css, src/styles/tokens.scss, src/lib/tokens.generated.ts');
}

const isMainModule = process.argv[1] && fileURLToPath(import.meta.url) === path.resolve(process.argv[1]);
if (isMainModule) {
  main();
}
