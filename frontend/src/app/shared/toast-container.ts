import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { CartStore } from '../core/cart.store';
import { ToastService } from '../core/toast.service';
import { Icon } from './icon';

@Component({
  selector: 'app-toast-container',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="stack" [class.shifted]="cart.drawerOpen()" role="status" aria-live="polite">
      @for (t of toasts.toasts(); track t.id) {
        <div class="toast" [class]="'toast ' + t.kind">
          <span class="icon">
            <app-icon [name]="t.kind === 'success' ? 'check-circle' : t.kind === 'error' ? 'alert-circle' : t.kind === 'warning' ? 'alert' : 'info'" [size]="18" />
          </span>
          <div class="body">
            <div class="title">{{ t.title }}</div>
            @if (t.message) { <div class="msg">{{ t.message }}</div> }
          </div>
          <button class="close" (click)="toasts.dismiss(t.id)" aria-label="Dismiss"><app-icon name="x" [size]="14" /></button>
        </div>
      }
    </div>
  `,
  styles: `
    .stack {
      position: fixed; right: 20px; bottom: 20px; z-index: 200;
      display: flex; flex-direction: column; gap: 10px; width: min(380px, calc(100vw - 40px));
      transition: right 0.3s var(--ease);
    }
    /* Keep toasts clear of the cart drawer so they never cover its actions. */
    .stack.shifted { right: calc(min(420px, 100vw) + 20px); }
    @media (max-width: 860px) {
      .stack.shifted { right: 20px; bottom: auto; top: 20px; }
    }
    .toast {
      display: flex; gap: 12px; align-items: flex-start;
      padding: 14px 14px 14px 16px; border-radius: 14px;
      background: #0f172a; color: #e2e8f0; box-shadow: var(--shadow-lg);
      animation: in 0.28s var(--ease);
    }
    .icon { margin-top: 1px; }
    .success .icon { color: #34d399; }
    .error .icon { color: #fb7185; }
    .warning .icon { color: #fbbf24; }
    .info .icon { color: #38bdf8; }
    .body { flex: 1; min-width: 0; }
    .title { font-weight: 600; font-size: 0.9rem; color: #fff; }
    .msg { font-size: 0.83rem; color: #94a3b8; margin-top: 2px; word-break: break-word; }
    .close {
      background: transparent; border: 0; color: #64748b; cursor: pointer; padding: 2px; border-radius: 6px;
      &:hover { color: #fff; background: rgba(255,255,255,0.08); }
    }
    @keyframes in { from { opacity: 0; transform: translateY(10px) scale(0.98); } }
  `
})
export class ToastContainer {
  protected readonly toasts = inject(ToastService);
  protected readonly cart = inject(CartStore);
}
