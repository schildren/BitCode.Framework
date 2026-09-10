# Guía — Localización / i18n (F7-11, Fase 7 — Plataforma Angular empresarial)

> Tarea de origen: F7-11 (Localización) del [Plan Maestro de BitCode](plan-maestro-bitcode-ia.md). Alcance:
> "Idiomas, fechas, moneda y zona horaria" -- un estado de locale/timezone activo, formateo consistente de
> fecha/moneda/número vía `Intl`, y un catálogo mínimo de traducciones extensible. No incluye traducir toda
> la UI existente de `workflow`/`documents`/`grid`/`forms` (fuera de alcance: esos componentes siguen con
> texto fijo en español, ver sección 5) ni `@angular/localize` (build-time i18n de Angular) -- se eligió un
> enfoque runtime basado en `Intl` + un catálogo simple, ver sección 6 (decisión de diseño).

## 1. Dónde vive cada pieza

Todo vive en `@bitcode/core` ("Configuración, errores, logging, HTTP y convenciones", Plan Maestro, tabla
de paquetes de Fase 7) -- no se creó un paquete `@bitcode/i18n` nuevo, porque la tabla de paquetes objetivo
de la fase no lo contempla y el tamaño del entregable no lo justifica (mismo patrón que F7-06, que agregó
`errors/` a `core`).

| Pieza | Ubicación |
|---|---|
| `BitcodeLocale`, `BITCODE_DEFAULT_LOCALE`, `BITCODE_SUPPORTED_LOCALES`, `BITCODE_TIME_ZONE` | `frontend/packages/core/src/lib/i18n/locale.model.ts` |
| `BitcodeLocaleService` (estado activo: locale + timeZone) | `frontend/packages/core/src/lib/i18n/locale.service.ts` |
| `formatBitcodeDate`/`formatBitcodeDateTime`/`formatBitcodeCurrency`/`formatBitcodeNumber` (funciones puras) | `frontend/packages/core/src/lib/i18n/formatters.ts` |
| Pipes `bcDate`/`bcDateTime`/`bcCurrency`/`bcNumber` | `frontend/packages/core/src/lib/i18n/pipes.ts` |
| `BitcodeTranslations`, `DEFAULT_BITCODE_TRANSLATIONS`, `provideBitcodeTranslations` | `frontend/packages/core/src/lib/i18n/translation.model.ts` |
| `BitcodeTranslationService` | `frontend/packages/core/src/lib/i18n/translation.service.ts` |
| Pipe `bcT` | `frontend/packages/core/src/lib/i18n/translate.pipe.ts` |
| Catálogo de errores por locale (`BITCODE_ERROR_MESSAGES_BY_LOCALE`, `provideBitcodeErrorMessagesForLocale`) | `frontend/packages/core/src/lib/errors/bitcode-error.model.ts` |

## 2. `BitcodeLocaleService`: el único estado de locale/timezone activo

Signal-based, `providedIn: 'root'`:

- `locale()` -- `BitcodeLocale` activo (`'es-AR' | 'en-US'` hoy). Arranca en el valor persistido en
  `localStorage['bitcode.locale']` si es uno soportado, si no en `BITCODE_DEFAULT_LOCALE` (`'es-AR'`).
- `timeZone()` -- IANA timezone activa. Arranca en `BITCODE_TIME_ZONE` (por defecto la del navegador,
  `Intl.DateTimeFormat().resolvedOptions().timeZone`; sobreescribible con `provideBitcodeTimeZone(tz)`, por
  ejemplo con la zona horaria del tenant/organización que devuelva el backend).
- `setLocale(locale)` / `setTimeZone(tz)` -- actualizan el signal y (para `setLocale`) persisten en
  `localStorage`.

Es **puramente de presentación**: ninguna regla de negocio debe depender de `locale()`/`timeZone()` (esas
viven en el backend, por las reglas duras de `convenciones.md`) -- es el mismo criterio que ya se aplicó en
F7-04 con RBAC/ABAC ("la UI no sustituye validación backend").

`BITCODE_LOCALE_STORAGE` es inyectable (por defecto `window.localStorage`, con fallback en memoria si
`window` no existe, p. ej. SSR) -- permite testear sin tocar `localStorage` real, mismo patrón usado en los
tests reales del resto del repo (nunca mocks superficiales que rompan si cambia el detalle interno).

## 3. Formateo: `Intl` envuelto, no reinventado

`formatBitcodeDate`/`formatBitcodeDateTime`/`formatBitcodeCurrency`/`formatBitcodeNumber` son funciones
puras (`locale` + `timeZone`/opciones explícitos, nunca implícitos del entorno) -- reutilizables fuera de
Angular y testeables sin `TestBed`. Los pipes (`bcDate`, `bcDateTime`, `bcCurrency`, `bcNumber`) las envuelven
inyectando `BitcodeLocaleService` automáticamente.

Detalle verificado con tests reales (no asumido): sin `dateStyle`/`timeStyle` explícitos, `formatBitcodeDate`
usa componentes numéricos explícitos (`day/month/year: 'numeric'`) en vez de `Intl.DateTimeFormat`'s
`dateStyle: 'short'`, porque este último trunca el año a 2 dígitos en varios locales (`15/3/26`) -- ambiguo
para datos de negocio. Un consumidor que sí quiera el estilo corto de `Intl` puede pasarlo explícito
(`{ dateStyle: 'short' }`).

`BitcodeCurrencyFormatOptions.currency` es **obligatorio**, a diferencia de `Intl.NumberFormat` (que
permite omitir `currency` con `style: 'currency'` y explota en runtime) -- la moneda de un monto es un dato
de negocio que debe venir siempre del backend junto con el monto, nunca inferirse del locale de la UI (un
usuario en `es-AR` puede estar viendo un monto en `USD`).

Los pipes son `pure: false` deliberadamente: reaccionan a que `BitcodeLocaleService` cambie de locale/tz en
caliente (p. ej. un selector de idioma en el shell), no sólo a que cambie el valor de entrada -- un pipe
`pure: true` normal de Angular no se reevaluaría si sólo cambia el servicio inyectado.

Uso típico en un componente standalone:

```ts
import { BitcodeCurrencyPipe, BitcodeDatePipe } from '@bitcode/core';

@Component({
  standalone: true,
  imports: [BitcodeDatePipe, BitcodeCurrencyPipe],
  template: `
    <span>{{ orden.fecha | bcDate }}</span>
    <span>{{ orden.total | bcCurrency: { currency: orden.moneda } }}</span>
  `,
})
export class OrdenResumenComponent { /* ... */ }
```

## 4. Traducciones: catálogo simple, no un framework de i18n completo

`BitcodeTranslationService.translate(key, params?)` resuelve una clave (`'common.save'`) contra el catálogo
del locale activo (`BITCODE_TRANSLATIONS`, reactivo vía `computed()` sobre `BitcodeLocaleService.locale()`),
con fallback en cascada: locale activo -> `BITCODE_DEFAULT_LOCALE` -> la clave misma (nunca lanza ni deja la
UI en blanco por una clave faltante -- una clave sin traducir queda visible como texto plano, detectable en
QA). Soporta interpolación simple `{{param}}`, sin pluralización ni ICU MessageFormat (deliberadamente fuera
de alcance -- ver sección 6).

`DEFAULT_BITCODE_TRANSLATIONS` en `@bitcode/core` sólo trae un puñado de claves transversales
(`common.save`, `common.cancel`, `common.confirm`, `common.delete`, `common.close`, `common.loading`,
`common.retry`, `common.search`, `common.noResults`) -- **no** es un catálogo completo de toda la UI de una
aplicación consumidora. `provideBitcodeTranslations(overrides)` mergea por clave (no reemplaza el catálogo
entero), así una app agrega sus propias claves de negocio en `app.config.ts` sin perder las de `@bitcode/core`:

```ts
provideBitcodeTranslations({
  'es-AR': { 'ordenes.titulo': 'Órdenes de compra' },
  'en-US': { 'ordenes.titulo': 'Purchase orders' },
})
```

El pipe `bcT` (también `pure: false`, mismo motivo que los de formateo) envuelve el servicio para templates.

## 5. Catálogo de mensajes de error por locale (cierra el pendiente de F7-06)

`docs/guia-frontend-errores.md` dejó explícito como pendiente: *"Sin i18n real de los mensajes del catálogo
(F7-11): `BITCODE_ERROR_MESSAGES` es overridable pero los valores por defecto están sólo en español."* Esta
tarea lo cierra sin romper el contrato existente:

- `BITCODE_ERROR_MESSAGES_BY_LOCALE: Record<BitcodeLocale, BitcodeErrorMessages>` -- catálogo `es-AR`
  (el `DEFAULT_BITCODE_ERROR_MESSAGES` ya existente, sin cambios) + `en-US` (traducción literal nueva).
- `provideBitcodeErrorMessagesForLocale(locale, overrides?)` -- variante de `provideBitcodeErrorMessages`
  que arranca del catálogo del `locale` indicado en vez de siempre `es-AR`. Cae a `es-AR` si se le pasa un
  locale sin catálogo propio (verificado con test, no sólo declarado).

**Limitación honesta, documentada igual que en F7-06:** `BITCODE_ERROR_MESSAGES` (el token que consume
`BitcodeErrorExperienceService`) **no es reactivo** -- se resuelve una sola vez al bootstrapear la app.
Si el usuario cambia de idioma en caliente con `BitcodeLocaleService.setLocale(...)`, los mensajes de error
NO se retraducen automáticamente (a diferencia de `bcT`/`BitcodeTranslationService`, que sí son reactivos
vía `computed()`). Retrofitear `BITCODE_ERROR_MESSAGES` a reactivo habría exigido cambiar su contrato público
(de `BitcodeErrorMessages` estático a una función/signal) -- un breaking change no justificado por el
alcance de esta tarea (regla dura de `convenciones.md`/sección 3.2 del plan: no cambiar contratos públicos
sin análisis de compatibilidad). Queda como candidato explícito para una tarea futura si un consumidor real
necesita cambio de idioma en caliente también para los mensajes de error.

## 6. Decisión de diseño: `Intl` + catálogo simple, no `@angular/localize`

El plan deja la elección de enfoque de i18n abierta ("Localización... Idiomas, fechas, moneda y zona
horaria"); no hay una fila de "decisión recomendada" explícita, pero corresponde tomarla acá sin bloquear la
tarea:

**Elegido:** estado runtime (`BitcodeLocaleService`, signal) + formateo vía `Intl` nativo + catálogo de
traducciones en memoria (`BITCODE_TRANSLATIONS`, `Record<clave, texto>`).

**Descartado, con motivo:** `@angular/localize` (el mecanismo oficial de Angular, basado en `$localize` +
archivos `.xlf`/extracción en build time, un bundle separado por idioma). Se descartó por tres razones
verificables en el estado actual del repo, no por preferencia:

1. **Requiere un build por idioma** (`"localize": true` en `angular.json`, un bundle `.xlf` por locale) --
   cambia la estrategia de build/deploy de los 7 paquetes + `apps/shell` del workspace (F7-01), un cambio
   arquitectónico de mayor alcance que el de esta tarea puntual.
2. **No soporta cambio de idioma en runtime sin recargar la página** (cada bundle es estático por idioma) --
   contradice el criterio de aceptación implícito de un selector de idioma dinámico en el shell (F7-05,
   "Menú dinámico... Actualización controlada" ya sienta el precedente de UI reactiva sin reload).
3. El volumen de texto a traducir hoy es bajo (la mayoría de los componentes de `workflow`/`documents`/
   `grid`/`forms` tienen texto fijo en español, ver limitación abajo) -- no justifica la inversión en
   tooling de extracción/build de `@angular/localize` todavía.

Si en el futuro el volumen de texto crece sustancialmente (traducción completa de una app de negocio
grande) y el cambio de idioma en runtime deja de ser un requisito, migrar a `@angular/localize` sería una
decisión de arquitectura nueva -- candidata a ADR propio en ese momento, no a resolverse retroactivamente
acá.

## 7. Cómo se probó

`frontend/packages/core/src/lib/i18n/*.spec.ts` (formatters, `BitcodeLocaleService`, `BitcodeTranslationService`,
pipes) + `frontend/packages/core/src/lib/errors/bitcode-error.model.locale.spec.ts` (catálogo de errores por
locale) -- 25 tests nuevos, todos con aserciones sobre el valor formateado real (no snapshots ciegos),
incluida una verificación explícita de que la zona horaria cambia el día calendario resultante para un mismo
instante UTC (`'2026-03-15T01:30:00Z'` cae en 15/3 en UTC pero 14/3 en `America/Argentina/Buenos_Aires`).

Comandos ejecutados:

```bash
cd frontend
npx nx run core:test
npx nx run core:lint
npx nx run-many -t build test lint
```

Resultado: 41 tests en `core` (25 nuevos de i18n + 16 preexistentes de errores/core), `core:lint` limpio, y
`nx run-many -t build test lint` sigue pasando para los 8 proyectos del workspace (25 tareas), sin
regresiones en `auth`/`ui`/`grid`/`forms`/`workflow`/`documents`/`shell`.

## 8. Limitaciones y pendientes explícitos (fuera de alcance de F7-11)

- **Ningún componente existente (`grid`, `forms`, `workflow`, `documents`, navigation shell) fue migrado
  para usar los pipes/el catálogo de traducciones** -- siguen con texto fijo en español. Migrarlos es
  trabajo mecánico de cada módulo, no de esta tarea (que entrega la infraestructura reutilizable). El
  catálogo de mensajes de error (`BITCODE_ERROR_MESSAGES_BY_LOCALE`) sí quedó resuelto porque F7-06 ya lo
  dejó como pendiente explícito de esta tarea.
- **`BITCODE_ERROR_MESSAGES` no es reactivo a un cambio de idioma en caliente** (ver sección 5) --
  `bcT`/`BitcodeTranslationService` sí lo son.
- **Sólo `es-AR`/`en-US` soportados hoy** (`BitcodeLocale`, `BITCODE_SUPPORTED_LOCALES`) -- agregar un
  locale nuevo implica extenderlo ahí y, opcionalmente, agregar su catálogo de traducciones/errores.
- **Sin pluralización ni ICU MessageFormat** en `BitcodeTranslationService` -- sólo interpolación simple
  `{{param}}` (ver sección 6, decisión de diseño).
- **`apps/shell` no incorporó un selector de idioma visual** -- la infraestructura (`BitcodeLocaleService.
  setLocale`) está lista para que cualquier componente de UI lo dispare; construir ese selector es trabajo
  de `@bitcode/ui`/consumo real, no de esta tarea de infraestructura de `@bitcode/core`.
