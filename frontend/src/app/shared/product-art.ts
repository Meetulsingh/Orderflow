import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { Icon } from './icon';

const THEMES: Record<string, { icon: string; from: string; to: string }> = {
  electronics: { icon: 'headphones', from: '#6366f1', to: '#22d3ee' },
  home: { icon: 'home', from: '#f59e0b', to: '#f43f5e' },
  apparel: { icon: 'shirt', from: '#10b981', to: '#0ea5e9' },
  books: { icon: 'book', from: '#8b5cf6', to: '#ec4899' },
  fitness: { icon: 'dumbbell', from: '#ef4444', to: '#f97316' },
  accessories: { icon: 'briefcase', from: '#0f172a', to: '#64748b' }
};

/** Name keywords → a more specific icon than the category default. */
const KEYWORD_ICONS: [RegExp, string][] = [
  [/headphone|earbud/i, 'headphones'],
  [/watch/i, 'watch'],
  [/webcam|camera/i, 'camera'],
  [/charger|power|battery/i, 'zap'],
  [/keyboard/i, 'keyboard'],
  [/lamp|light/i, 'lamp'],
  [/coffee|brew|pour-over/i, 'coffee'],
  [/blanket|throw/i, 'layers'],
  [/plant/i, 'leaf'],
  [/jacket|tee|shirt|hoodie/i, 'shirt'],
  [/shoe|sneaker|trainer/i, 'footprints'],
  [/yoga|mat/i, 'activity'],
  [/dumbbell|weight/i, 'dumbbell'],
  [/backpack|bag/i, 'briefcase'],
  [/sunglass/i, 'sun'],
  [/book|guide/i, 'book']
];

export function categoryTheme(category: string) {
  return THEMES[category.toLowerCase()] ?? { icon: 'package', from: '#4f46e5', to: '#7c3aed' };
}

function productIcon(name: string, category: string): string {
  return KEYWORD_ICONS.find(([pattern]) => pattern.test(name))?.[1] ?? categoryTheme(category).icon;
}

/** Product visual: the product image if one is set, otherwise generated category artwork. */
@Component({
  selector: 'app-product-art',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (imageUrl() && !failed()) {
      <img [src]="imageUrl()" [alt]="name()" loading="lazy" (error)="failed.set(true)" />
    } @else {
      <div class="art" [style.background]="gradient()">
        <span class="initial">{{ initial() }}</span>
        <app-icon [name]="icon()" [size]="iconSize()" [stroke]="1.5" />
      </div>
    }
  `,
  styles: `
    :host { display: block; width: 100%; height: 100%; overflow: hidden; border-radius: inherit; }
    img { width: 100%; height: 100%; object-fit: cover; display: block; }
    .art {
      position: relative; width: 100%; height: 100%;
      display: grid; place-items: center; color: rgba(255,255,255,0.95);
      isolation: isolate;
    }
    .art::before {
      content: ''; position: absolute; inset: 0; z-index: -1;
      background:
        radial-gradient(circle at 20% 15%, rgba(255,255,255,0.35), transparent 40%),
        radial-gradient(circle at 85% 90%, rgba(0,0,0,0.18), transparent 45%);
    }
    .initial {
      position: absolute; right: 12px; bottom: -14px;
      font-size: 5.5rem; font-weight: 800; letter-spacing: -0.06em;
      color: rgba(255,255,255,0.14); line-height: 1;
    }
  `
})
export class ProductArt {
  readonly name = input.required<string>();
  readonly category = input.required<string>();
  readonly imageUrl = input<string | null | undefined>(null);
  readonly iconSize = input(44);

  protected readonly failed = signal(false);
  protected readonly theme = computed(() => categoryTheme(this.category()));
  protected readonly icon = computed(() => productIcon(this.name(), this.category()));
  protected readonly gradient = computed(() => `linear-gradient(135deg, ${this.theme().from}, ${this.theme().to})`);
  protected readonly initial = computed(() => this.name().charAt(0).toUpperCase());
}
