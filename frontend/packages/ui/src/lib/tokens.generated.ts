/**
 * BitCode Design Tokens — AUTO-GENERADO por tokens/build-tokens.mjs. No editar a mano.
 *
 * Mapa de tokens -> referencia a la custom property CSS correspondiente (`var(--bc-*)`),
 * NO el valor resuelto: el valor real depende del tema activo (`:root` / `[data-theme=
 * "dark"]`) en tiempo de ejecución, definido en styles/tokens.css. Usar este mapa desde
 * TypeScript cuando se necesite referenciar un token de forma tipada (p. ej. al dibujar
 * en un <canvas> o pasar un color a una librería de charts) sin hardcodear el nombre de
 * la variable CSS a mano.
 */

export const tokens = {
  "space": {
    "0": "var(--bc-space-0)",
    "1": "var(--bc-space-1)",
    "2": "var(--bc-space-2)",
    "3": "var(--bc-space-3)",
    "4": "var(--bc-space-4)",
    "5": "var(--bc-space-5)",
    "6": "var(--bc-space-6)",
    "8": "var(--bc-space-8)",
    "10": "var(--bc-space-10)",
    "12": "var(--bc-space-12)",
    "16": "var(--bc-space-16)"
  },
  "fontFamily": {
    "sans": "var(--bc-font-family-sans)",
    "mono": "var(--bc-font-family-mono)"
  },
  "fontSize": {
    "xs": "var(--bc-font-size-xs)",
    "sm": "var(--bc-font-size-sm)",
    "base": "var(--bc-font-size-base)",
    "lg": "var(--bc-font-size-lg)",
    "xl": "var(--bc-font-size-xl)",
    "2xl": "var(--bc-font-size-2xl)",
    "3xl": "var(--bc-font-size-3xl)",
    "4xl": "var(--bc-font-size-4xl)"
  },
  "fontWeight": {
    "regular": "var(--bc-font-weight-regular)",
    "medium": "var(--bc-font-weight-medium)",
    "semibold": "var(--bc-font-weight-semibold)",
    "bold": "var(--bc-font-weight-bold)"
  },
  "lineHeight": {
    "tight": "var(--bc-line-height-tight)",
    "normal": "var(--bc-line-height-normal)",
    "relaxed": "var(--bc-line-height-relaxed)"
  },
  "radius": {
    "sm": "var(--bc-radius-sm)",
    "md": "var(--bc-radius-md)",
    "lg": "var(--bc-radius-lg)",
    "full": "var(--bc-radius-full)"
  },
  "shadow": {
    "sm": "var(--bc-shadow-sm)",
    "md": "var(--bc-shadow-md)",
    "lg": "var(--bc-shadow-lg)"
  },
  "duration": {
    "fast": "var(--bc-duration-fast)",
    "base": "var(--bc-duration-base)",
    "slow": "var(--bc-duration-slow)"
  },
  "easing": {
    "standard": "var(--bc-easing-standard)"
  },
  "opacity": {
    "hover": "var(--bc-opacity-hover)",
    "pressed": "var(--bc-opacity-pressed)",
    "disabled": "var(--bc-opacity-disabled)",
    "focusRing": "var(--bc-opacity-focus-ring)"
  },
  "color": {
    "surface": "var(--bc-color-surface)",
    "surfaceElevated": "var(--bc-color-surface-elevated)",
    "surfaceSunken": "var(--bc-color-surface-sunken)",
    "border": "var(--bc-color-border)",
    "borderStrong": "var(--bc-color-border-strong)",
    "textPrimary": "var(--bc-color-text-primary)",
    "textSecondary": "var(--bc-color-text-secondary)",
    "textDisabled": "var(--bc-color-text-disabled)",
    "textInverse": "var(--bc-color-text-inverse)",
    "brandPrimary": "var(--bc-color-brand-primary)",
    "brandPrimaryHover": "var(--bc-color-brand-primary-hover)",
    "brandPrimaryActive": "var(--bc-color-brand-primary-active)",
    "brandPrimaryText": "var(--bc-color-brand-primary-text)",
    "onBrandPrimary": "var(--bc-color-on-brand-primary)",
    "focusRing": "var(--bc-color-focus-ring)",
    "success": "var(--bc-color-success)",
    "successBg": "var(--bc-color-success-bg)",
    "successBorder": "var(--bc-color-success-border)",
    "warning": "var(--bc-color-warning)",
    "warningBg": "var(--bc-color-warning-bg)",
    "warningBorder": "var(--bc-color-warning-border)",
    "error": "var(--bc-color-error)",
    "errorBg": "var(--bc-color-error-bg)",
    "errorBorder": "var(--bc-color-error-border)",
    "info": "var(--bc-color-info)",
    "infoBg": "var(--bc-color-info-bg)",
    "infoBorder": "var(--bc-color-info-border)"
  }
} as const;

export type BitCodeTokens = typeof tokens;
