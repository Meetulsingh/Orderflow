import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { Product } from './models';
import { ToastService } from './toast.service';

export interface CartLine {
  productId: string;
  name: string;
  category: string;
  price: number;
  stock: number;
  quantity: number;
}

const STORAGE_KEY = 'orderflow.cart';
export const MAX_QTY_PER_LINE = 20;

/** Signal-based cart state, persisted to localStorage. */
@Injectable({ providedIn: 'root' })
export class CartStore {
  private readonly toast = inject(ToastService);

  readonly lines = signal<CartLine[]>(this.restore());
  readonly drawerOpen = signal(false);

  readonly count = computed(() => this.lines().reduce((n, l) => n + l.quantity, 0));
  readonly subtotal = computed(() => this.lines().reduce((sum, l) => sum + l.price * l.quantity, 0));
  readonly isEmpty = computed(() => this.lines().length === 0);

  constructor() {
    effect(() => {
      const lines = this.lines();
      try {
        localStorage.setItem(STORAGE_KEY, JSON.stringify(lines));
      } catch { /* storage unavailable */ }
    });
  }

  add(product: Product, quantity = 1): void {
    const existing = this.lines().find(l => l.productId === product.id);
    const current = existing?.quantity ?? 0;
    const limit = Math.min(product.stock, MAX_QTY_PER_LINE);
    if (current + quantity > limit) {
      this.toast.warning('Quantity limit reached', `You can add up to ${limit} of "${product.name}".`);
      return;
    }

    if (existing) {
      this.setQuantity(product.id, current + quantity);
    } else {
      this.lines.update(lines => [
        ...lines,
        { productId: product.id, name: product.name, category: product.category, price: product.price, stock: product.stock, quantity }
      ]);
    }
    // Feedback comes from the animated cart badge in the top bar (a toast would cover the cart drawer).
  }

  setQuantity(productId: string, quantity: number): void {
    if (quantity <= 0) return this.remove(productId);
    this.lines.update(lines =>
      lines.map(l => (l.productId === productId ? { ...l, quantity: Math.min(quantity, l.stock, MAX_QTY_PER_LINE) } : l))
    );
  }

  remove(productId: string): void {
    this.lines.update(lines => lines.filter(l => l.productId !== productId));
  }

  clear(): void {
    this.lines.set([]);
  }

  private restore(): CartLine[] {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      return raw ? (JSON.parse(raw) as CartLine[]) : [];
    } catch {
      return [];
    }
  }
}
