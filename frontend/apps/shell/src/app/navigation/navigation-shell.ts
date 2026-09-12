import { Component, inject, signal } from '@angular/core';
import { RouterModule } from '@angular/router';
import { BitcodeMenuItem, BitcodeMenuService } from '@bitcode/ui';

/**
 * Componente de navegación real de `apps/shell` (F7-05): renderiza el árbol ya filtrado por permisos que
 * expone `BitcodeMenuService` (`@bitcode/ui`), con soporte de expandir/colapsar grupos.
 *
 * Simplificación documentada (ver `docs/guia-frontend-navigation.md`): el estado de expandido/colapsado
 * vive únicamente en memoria del componente (`Set<string>`), no se persiste entre sesiones/recargas
 * (no hay `localStorage` ni preferencia de usuario del lado servidor todavía).
 */
@Component({
  selector: 'app-navigation-shell',
  imports: [RouterModule],
  templateUrl: './navigation-shell.html',
  styleUrl: './navigation-shell.scss',
})
export class NavigationShell {
  private readonly menuService = inject(BitcodeMenuService);

  readonly menu = this.menuService.menu;

  private readonly expandedGroupIds = signal<ReadonlySet<string>>(new Set());

  isExpanded(item: BitcodeMenuItem): boolean {
    return this.expandedGroupIds().has(item.id);
  }

  isGroup(item: BitcodeMenuItem): boolean {
    return !!item.children && item.children.length > 0;
  }

  toggleGroup(item: BitcodeMenuItem): void {
    const next = new Set(this.expandedGroupIds());
    if (next.has(item.id)) {
      next.delete(item.id);
    } else {
      next.add(item.id);
    }
    this.expandedGroupIds.set(next);
  }
}
