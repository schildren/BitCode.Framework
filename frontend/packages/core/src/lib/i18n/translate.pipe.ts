import { Pipe, PipeTransform, inject } from '@angular/core';
import { BitcodeTranslationService } from './translation.service';

/** `impure` (`pure: false`) por el mismo motivo que los pipes de formateo: debe reevaluarse cuando cambia
 * el locale activo, no sólo cuando cambia la clave de entrada. */
@Pipe({ name: 'bcT', standalone: true, pure: false })
export class BitcodeTranslatePipe implements PipeTransform {
  private readonly translation = inject(BitcodeTranslationService);

  transform(key: string, params?: Readonly<Record<string, string | number>>): string {
    return this.translation.translate(key, params);
  }
}
