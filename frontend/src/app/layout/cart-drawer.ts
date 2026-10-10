import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { CartStore } from '../core/cart.store';
import { Icon } from '../shared/icon';
import { ProductArt } from '../shared/product-art';

@Component({
  selector: 'app-cart-drawer',
  imports: [Icon, ProductArt, CurrencyPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (cart.drawerOpen()) {
      <div class="backdrop" (click)="cart.drawerOpen.set(false)"></div>
      <aside class="drawer" role="dialog" aria-label="Shopping cart">
        <header class="row-between">
          <div>
            <h2>Your cart</h2>
            <p class="muted small">{{ cart.count() }} item{{ cart.count() === 1 ? '' : 's' }}</p>
          </div>
          <button class="btn btn-ghost btn-icon" (click)="cart.drawerOpen.set(false)" aria-label="Close cart">
            <app-icon name="x" />
          </button>
        </header>

        @if (cart.isEmpty()) {
          <div class="empty-state grow">
            <div class="empty-icon"><app-icon name="cart" [size]="26" /></div>
            <h3>Your cart is empty</h3>
            <p class="small">Add a few products from the catalog to start an order.</p>
            <button class="btn btn-soft" (click)="cart.drawerOpen.set(false)">Browse products</button>
          </div>
        } @else {
          <div class="lines">
            @for (line of cart.lines(); track line.productId) {
              <div class="line">
                <div class="thumb"><app-product-art [name]="line.name" [category]="line.category" [imageUrl]="line.imageUrl" [iconSize]="22" /></div>
                <div class="grow info">
                  <div class="strong name">{{ line.name }}</div>
                  <div class="subtle xs">{{ line.price | currency }} each</div>
                  <div class="qty">
                    <button class="btn btn-sm btn-icon" (click)="cart.setQuantity(line.productId, line.quantity - 1)" aria-label="Decrease"><app-icon name="minus" [size]="14" /></button>
                    <span>{{ line.quantity }}</span>
                    <button class="btn btn-sm btn-icon" (click)="cart.setQuantity(line.productId, line.quantity + 1)" [disabled]="line.quantity >= line.stock" aria-label="Increase"><app-icon name="plus" [size]="14" /></button>
                  </div>
                </div>
                <div class="end">
                  <div class="strong">{{ line.price * line.quantity | currency }}</div>
                  <button class="btn btn-ghost btn-sm btn-icon" (click)="cart.remove(line.productId)" aria-label="Remove"><app-icon name="trash" [size]="15" /></button>
                </div>
              </div>
            }
          </div>

          <footer>
            <div class="row-between total"><span class="muted">Subtotal</span><span class="strong">{{ cart.subtotal() | currency }}</span></div>
            <div class="row-between small"><span class="muted">Shipping</span><span class="badge badge-success">Free</span></div>
            <button class="btn btn-primary btn-lg btn-block" (click)="checkout()">
              Checkout <app-icon name="arrow-right" [size]="16" />
            </button>
          </footer>
        }
      </aside>
    }
  `,
  styles: `
    .drawer {
      position: fixed; top: 0; right: 0; bottom: 0; z-index: 90;
      width: min(420px, 100vw); background: var(--surface);
      display: flex; flex-direction: column; box-shadow: var(--shadow-lg);
      animation: slide 0.3s var(--ease);
    }
    @keyframes slide { from { transform: translateX(100%); } }
    header { padding: 20px 20px 16px; border-bottom: 1px solid var(--border); }
    .lines { flex: 1; overflow-y: auto; padding: 8px 20px; }
    .line { display: flex; gap: 14px; padding: 14px 0; border-bottom: 1px solid var(--border); }
    .line:last-child { border-bottom: 0; }
    .thumb { width: 64px; height: 64px; border-radius: 12px; overflow: hidden; flex-shrink: 0; }
    .info { min-width: 0; }
    .name { font-size: 0.9rem; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .qty { display: flex; align-items: center; gap: 10px; margin-top: 8px; font-weight: 600; font-size: 0.88rem; }
    .end { display: flex; flex-direction: column; align-items: flex-end; justify-content: space-between; }
    footer { padding: 18px 20px 22px; border-top: 1px solid var(--border); display: flex; flex-direction: column; gap: 12px; background: var(--surface-2); }
    .total { font-size: 1.05rem; }
  `
})
export class CartDrawer {
  protected readonly cart = inject(CartStore);
  private readonly router = inject(Router);

  checkout() {
    this.cart.drawerOpen.set(false);
    this.router.navigateByUrl('/checkout');
  }
}
