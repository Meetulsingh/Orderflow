import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../core/auth/auth.service';
import { CartStore } from '../core/cart.store';
import { OrderHubService } from '../core/order-hub.service';
import { WarmupService } from '../core/warmup.service';
import { Icon } from '../shared/icon';
import { AssistantPanel } from '../features/assistant/assistant-panel';
import { CartDrawer } from './cart-drawer';

@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, Icon, CartDrawer, AssistantPanel],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header class="topbar">
      <div class="inner">
        <a routerLink="/products" class="brand">
          <span class="logo"><img src="favicon.svg" alt="" width="30" height="30" /></span>
          <span class="name">OrderFlow</span>
        </a>

        <nav class="nav">
          <a routerLink="/products" routerLinkActive="active"><app-icon name="package" [size]="16" /><span>Catalog</span></a>
          @if (auth.isAuthenticated()) {
            <a routerLink="/orders" routerLinkActive="active"><app-icon name="activity" [size]="16" /><span>Orders</span></a>
          }
          @if (auth.isAdmin()) {
            <a routerLink="/admin/logs" routerLinkActive="active"><app-icon name="server" [size]="16" /><span>System</span></a>
          }
        </nav>

        <div class="actions">
          @if (auth.isAuthenticated()) {
            <span class="live hidden-sm" [class]="'live hidden-sm ' + hub.state()" [title]="'Real-time channel: ' + hub.state()">
              <span class="dot" [class.pulse]="hub.state() === 'connected'"></span>
              {{ hub.state() === 'connected' ? 'Live' : hub.state() === 'disconnected' ? 'Offline' : 'Connecting' }}
            </span>
          }

          <button class="btn btn-ghost btn-icon cart-btn" (click)="cart.drawerOpen.set(true)" aria-label="Open cart">
            <app-icon name="cart" [size]="20" />
            @if (cart.count() > 0) {
              <!-- Re-created on every change so the bump animation replays. -->
              @for (c of [cart.count()]; track c) { <span class="count">{{ c }}</span> }
            }
          </button>

          @if (auth.user(); as user) {
            <div class="user">
              <button class="avatar-btn" (click)="menuOpen.set(!menuOpen())" aria-label="Account menu">
                <span class="avatar">{{ initials() }}</span>
                <span class="hidden-sm who">
                  <span class="strong">{{ user.fullName }}</span>
                  <span class="xs subtle">{{ user.role }}</span>
                </span>
                <app-icon name="chevron-down" [size]="14" class="hidden-sm" />
              </button>
              @if (menuOpen()) {
                <div class="menu-backdrop" (click)="menuOpen.set(false)"></div>
                <div class="menu">
                  <div class="menu-head">
                    <div class="strong">{{ user.fullName }}</div>
                    <div class="xs muted">{{ user.email }}</div>
                  </div>
                  <a routerLink="/orders" (click)="menuOpen.set(false)"><app-icon name="activity" [size]="16" />My orders</a>
                  @if (auth.isAdmin()) {
                    <a routerLink="/admin/logs" (click)="menuOpen.set(false)"><app-icon name="server" [size]="16" />System monitor</a>
                  }
                  <button (click)="logout()"><app-icon name="logout" [size]="16" />Sign out</button>
                </div>
              }
            </div>
          } @else {
            <a routerLink="/login" class="btn btn-dark btn-sm"><app-icon name="login" [size]="15" />Sign in</a>
          }
        </div>
      </div>
    </header>

    @if (warmup.phase() === 'waking' && !warmup.dismissed()) {
      <div class="warmup">
        <span class="spinner"></span>
        <span>
          <strong>Waking up the microservices…</strong>
          <span class="hidden-sm"> They sleep on free-tier hosting when idle — the first load can take up to a minute.</span>
        </span>
        <span class="badge badge-outline">{{ warmup.readyCount() }}/{{ warmup.total() }} ready</span>
        <button class="btn btn-ghost btn-sm btn-icon" (click)="warmup.dismissed.set(true)" aria-label="Dismiss"><app-icon name="x" [size]="14" /></button>
      </div>
    }

    <main>
      <router-outlet />
    </main>

    <footer class="site-footer">
      <span>OrderFlow · .NET 10 microservices · RabbitMQ · Redis · PostgreSQL · Angular</span>
    </footer>

    <app-cart-drawer />
    <app-assistant-panel />
  `,
  styles: `
    :host { display: flex; flex-direction: column; min-height: 100vh; }
    main { flex: 1; }
    .topbar {
      position: sticky; top: 0; z-index: 50; height: var(--topbar-h);
      background: rgba(255,255,255,0.82); backdrop-filter: saturate(180%) blur(14px);
      border-bottom: 1px solid var(--border);
    }
    .inner { max-width: 1240px; margin: 0 auto; height: 100%; padding: 0 24px; display: flex; align-items: center; gap: 28px; }
    .brand { display: flex; align-items: center; gap: 10px; color: var(--text); text-decoration: none !important; }
    .logo { display: grid; place-items: center; }
    .name { font-weight: 750; font-size: 1.08rem; letter-spacing: -0.03em; }
    .nav { display: flex; gap: 4px; }
    .nav a {
      display: inline-flex; align-items: center; gap: 7px; height: 36px; padding: 0 12px; border-radius: 9px;
      color: var(--text-2); font-weight: 550; font-size: 0.9rem; text-decoration: none;
      &:hover { background: var(--surface-3); color: var(--text); }
      &.active { background: var(--primary-50); color: var(--primary); }
    }
    .actions { margin-left: auto; display: flex; align-items: center; gap: 10px; }
    .live {
      display: inline-flex; align-items: center; gap: 7px; font-size: 0.78rem; font-weight: 600;
      padding: 5px 10px; border-radius: 999px; background: var(--surface-3); color: var(--text-3);
      &.connected { background: var(--success-50); color: var(--success); }
      &.connecting, &.reconnecting { background: var(--warning-50); color: var(--warning); }
    }
    .cart-btn { position: relative; }
    .count {
      position: absolute; top: 2px; right: 0; min-width: 18px; height: 18px; padding: 0 5px;
      border-radius: 999px; background: var(--primary); color: #fff; font-size: 0.68rem; font-weight: 700;
      display: grid; place-items: center; border: 2px solid #fff; animation: bump 0.35s var(--ease);
    }
    @keyframes bump { 0% { transform: scale(0.6); } 60% { transform: scale(1.25); } 100% { transform: scale(1); } }
    .user { position: relative; }
    .avatar-btn {
      display: flex; align-items: center; gap: 10px; background: transparent; border: 0; cursor: pointer;
      padding: 4px 8px 4px 4px; border-radius: 999px; color: var(--text-2);
      &:hover { background: var(--surface-3); }
    }
    .avatar {
      width: 34px; height: 34px; border-radius: 50%; display: grid; place-items: center;
      background: linear-gradient(135deg, var(--primary), var(--accent)); color: #fff; font-weight: 700; font-size: 0.8rem;
    }
    .who { display: flex; flex-direction: column; align-items: flex-start; line-height: 1.2; font-size: 0.85rem; color: var(--text); }
    .menu-backdrop { position: fixed; inset: 0; z-index: 60; }
    .menu {
      position: absolute; right: 0; top: calc(100% + 8px); z-index: 61; width: 240px;
      background: var(--surface); border: 1px solid var(--border); border-radius: 14px; box-shadow: var(--shadow-lg);
      padding: 6px; animation: slide-up 0.18s var(--ease);
      a, button {
        display: flex; align-items: center; gap: 10px; width: 100%; padding: 9px 10px; border-radius: 9px;
        background: transparent; border: 0; color: var(--text); font-size: 0.88rem; cursor: pointer; text-decoration: none;
        &:hover { background: var(--surface-3); }
      }
    }
    .menu-head { padding: 10px 10px 12px; border-bottom: 1px solid var(--border); margin-bottom: 6px; }
    .warmup {
      display: flex; align-items: center; justify-content: center; gap: 12px; flex-wrap: wrap;
      padding: 10px 24px; font-size: 0.86rem; color: #3730a3;
      background: linear-gradient(90deg, #eef2ff, #f5f3ff); border-bottom: 1px solid var(--primary-100);
    }
    .site-footer {
      text-align: center; padding: 24px; font-size: 0.8rem; color: var(--text-3); border-top: 1px solid var(--border);
    }
    @media (max-width: 720px) {
      .inner { gap: 12px; padding: 0 14px; }
      .name { display: none; }
      .nav a span { display: none; }
    }
  `
})
export class Shell {
  protected readonly auth = inject(AuthService);
  protected readonly cart = inject(CartStore);
  protected readonly hub = inject(OrderHubService);
  protected readonly warmup = inject(WarmupService);
  protected readonly menuOpen = signal(false);

  protected readonly initials = computed(() =>
    (this.auth.user()?.fullName ?? '?')
      .split(' ')
      .map(p => p[0])
      .join('')
      .slice(0, 2)
      .toUpperCase()
  );


  logout() {
    this.menuOpen.set(false);
    this.auth.logout();
  }
}
