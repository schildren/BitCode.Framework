export * from './lib/core/core';

// F7-06: mapeo de ProblemDetails, correlation id y "error experience" consistente.
export * from './lib/errors/problem-details.model';
export * from './lib/errors/bitcode-error.model';
export * from './lib/errors/bitcode-http-error';
export * from './lib/errors/error-experience.service';
export * from './lib/errors/error.interceptor';

// F7-11: idiomas, fechas, moneda y zona horaria.
export * from './lib/i18n/locale.model';
export * from './lib/i18n/locale.service';
export * from './lib/i18n/formatters';
export * from './lib/i18n/pipes';
export * from './lib/i18n/translation.model';
export * from './lib/i18n/translation.service';
export * from './lib/i18n/translate.pipe';

// F7-12: harness de accesibilidad (axe-core) -- NO se exporta desde este índice principal a propósito
// (F7-14, hallazgo real de análisis de bundle): `axe-core` es una dependencia de sólo-test, y exportarla
// acá la incluía en el bundle de producción de cualquier consumidor de `@bitcode/core` (confirmado con
// `nx run shell:build:production`: warning "Module 'axe-core' used by ... is not ESM" + peso extra en el
// chunk inicial). Se importa vía el subpath `@bitcode/core/testing` (ver `tsconfig.base.json`), que
// ningún código de aplicación real debería importar -- sólo specs. Ver `docs/guia-frontend-performance.md`.

// F7-13: web vitals, errores, trazas y contexto ("frontend telemetry").
export * from './lib/telemetry/telemetry.model';
export * from './lib/telemetry/correlation';
export * from './lib/telemetry/telemetry.service';
export * from './lib/telemetry/telemetry.interceptor';
export * from './lib/telemetry/web-vitals';
export * from './lib/telemetry/provide-telemetry';
