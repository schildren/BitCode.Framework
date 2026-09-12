import { Pipe, PipeTransform, inject } from '@angular/core';
import { BitcodeLocaleService } from './locale.service';
import {
  BitcodeCurrencyFormatOptions,
  BitcodeDateFormatOptions,
  BitcodeDateTimeFormatOptions,
  BitcodeNumberFormatOptions,
  formatBitcodeCurrency,
  formatBitcodeDate,
  formatBitcodeDateTime,
  formatBitcodeNumber,
} from './formatters';

/**
 * Pipes standalone de F7-11. Son `pure: false` deliberadamente: si `BitcodeLocaleService.setLocale`/
 * `setTimeZone` cambian en tiempo de ejecución (p. ej. un selector de idioma en el shell), la vista debe
 * reformatearse sin recargar la página -- un pipe puro sólo se reevalúa cuando cambia el valor de entrada
 * (`value`), no cuando cambia un servicio inyectado.
 */

@Pipe({ name: 'bcDate', standalone: true, pure: false })
export class BitcodeDatePipe implements PipeTransform {
  private readonly localeService = inject(BitcodeLocaleService);

  transform(value: Date | string | number | null | undefined, options: BitcodeDateFormatOptions = {}): string {
    if (value === null || value === undefined || value === '') {
      return '';
    }
    return formatBitcodeDate(value, this.localeService.locale(), {
      timeZone: this.localeService.timeZone(),
      ...options,
    });
  }
}

@Pipe({ name: 'bcDateTime', standalone: true, pure: false })
export class BitcodeDateTimePipe implements PipeTransform {
  private readonly localeService = inject(BitcodeLocaleService);

  transform(value: Date | string | number | null | undefined, options: BitcodeDateTimeFormatOptions = {}): string {
    if (value === null || value === undefined || value === '') {
      return '';
    }
    return formatBitcodeDateTime(value, this.localeService.locale(), {
      timeZone: this.localeService.timeZone(),
      ...options,
    });
  }
}

@Pipe({ name: 'bcCurrency', standalone: true, pure: false })
export class BitcodeCurrencyPipe implements PipeTransform {
  private readonly localeService = inject(BitcodeLocaleService);

  transform(value: number | null | undefined, options: BitcodeCurrencyFormatOptions): string {
    if (value === null || value === undefined) {
      return '';
    }
    return formatBitcodeCurrency(value, this.localeService.locale(), options);
  }
}

@Pipe({ name: 'bcNumber', standalone: true, pure: false })
export class BitcodeNumberPipe implements PipeTransform {
  private readonly localeService = inject(BitcodeLocaleService);

  transform(value: number | null | undefined, options: BitcodeNumberFormatOptions = {}): string {
    if (value === null || value === undefined) {
      return '';
    }
    return formatBitcodeNumber(value, this.localeService.locale(), options);
  }
}
