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

// F7-12: harness de accesibilidad (axe-core) compartido para specs de otros paquetes.
export * from './lib/testing/a11y-harness';
