import axe, { AxeResults, ImpactValue, Result, RunOptions } from 'axe-core';

/**
 * Envoltorio de `axe-core` para specs de accesibilidad de F7-12 -- deliberadamente el ÚNICO lugar del
 * frontend que decide qué reglas se corren y qué severidad bloquea un test, para que "cero hallazgos
 * críticos" (criterio de aceptación literal) signifique lo mismo en todos los paquetes, no algo distinto
 * por componente.
 *
 * **Limitación honesta (jsdom, no un browser real):** los tests de este repo corren en Vitest+jsdom (ver
 * `environment: 'jsdom'` en cada `vite.config.mts`), no en un browser real -- jsdom no calcula layout ni
 * estilos computados reales, así que reglas que dependen de eso (`color-contrast`, `focus-order-semantics`
 * en algunos casos, tamaño de área táctil) no pueden evaluarse de forma confiable y quedan deshabilitadas
 * acá explícitamente (`BITCODE_A11Y_DISABLED_RULES`), no silenciadas por severidad. Esto NO reemplaza una
 * auditoría con un browser real (p. ej. `axe-core` + Playwright, o un lector de pantalla real) -- ver
 * `docs/guia-frontend-a11y.md` sección de limitaciones.
 */
export const BITCODE_A11Y_DISABLED_RULES: readonly string[] = [
  'color-contrast', // requiere estilos computados/layout real, no confiable en jsdom.
  'target-size', // área táctil mínima -- requiere layout real (getBoundingClientRect confiable).
];

/** Severidades que F7-12 trata como "hallazgo crítico" -- el resto (`moderate`/`minor`) se reporta pero no
 * hace fallar el test, para no bloquear la fase con hallazgos menores que no son parte del criterio de
 * aceptación literal ("Cero hallazgos críticos"). */
export const BITCODE_A11Y_CRITICAL_IMPACTS: readonly ImpactValue[] = ['critical', 'serious'];

export async function runBitcodeA11yCheck(element: Element, options: RunOptions = {}): Promise<AxeResults> {
  return axe.run(element, {
    rules: Object.fromEntries(BITCODE_A11Y_DISABLED_RULES.map((ruleId) => [ruleId, { enabled: false }])),
    ...options,
  });
}

export function criticalA11yViolations(results: AxeResults): readonly Result[] {
  return results.violations.filter((violation) =>
    BITCODE_A11Y_CRITICAL_IMPACTS.includes((violation.impact ?? 'minor') as ImpactValue),
  );
}

/** Mensaje legible para un `expect(...).toEqual([])` fallido -- sin esto, un fallo de axe sólo muestra
 * `expected [...] to equal []`, sin decir QUÉ regla falló ni en qué nodo, forzando a re-correr con
 * `--verbose` para depurar. */
export function describeA11yViolations(violations: readonly Result[]): string {
  return violations
    .map((violation) => {
      const targets = violation.nodes.map((node) => node.target.join(' ')).join(', ');
      return `[${violation.impact}] ${violation.id}: ${violation.help} (${violation.helpUrl}) -- nodos: ${targets}`;
    })
    .join('\n');
}
