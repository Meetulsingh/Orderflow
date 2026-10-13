import { CurrencyPipe, DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { OrdersApi } from '../../core/api.service';
import { AuthService } from '../../core/auth/auth.service';
import { Order, OrderStatus } from '../../core/models';
import { OrderHubService } from '../../core/order-hub.service';
import { ToastService } from '../../core/toast.service';
import { Icon } from '../../shared/icon';
import { StatusBadge } from '../../shared/status-badge';
import { TimeAgoPipe } from '../../shared/time-ago.pipe';

type Filter = 'all' | 'active' | 'Completed' | 'Failed';

const PIPELINE: { key: string; label: string; icon: string }[] = [
  { key: 'placed', label: 'Order placed', icon: 'package' },
  { key: 'payment', label: 'Payment', icon: 'card' },
  { key: 'done', label: 'Outcome', icon: 'check-circle' }
];

@Component({
  selector: 'app-orders-page',
  imports: [RouterLink, CurrencyPipe, DatePipe, Icon, StatusBadge, TimeAgoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page">
      <div class="page-header">
        <div>
          <span class="eyebrow"><app-icon name="activity" [size]="14" /> Live order tracking</span>
          <h1>{{ scope() === 'all' ? 'All orders' : 'My orders' }}</h1>
          <p class="subtitle">Status changes are pushed from the Ordering service over SignalR the moment payment events are consumed from RabbitMQ — no polling.</p>
        </div>
        <div class="row wrap">
          <span class="live" [class]="'live ' + hub.state()">
            <span class="dot" [class.pulse]="hub.state() === 'connected'"></span>
            {{ hub.state() === 'connected' ? 'Live via SignalR' : hub.state() === 'disconnected' ? 'Real-time offline' : 'Connecting…' }}
          </span>
          <button class="btn btn-sm" (click)="load()" [disabled]="loading()"><app-icon name="refresh" [size]="14" /> Refresh</button>
        </div>
      </div>

      <div class="toolbar">
        <div class="tabs">
          @for (f of filters; track f.id) {
            <button class="chip" [class.active]="filter() === f.id" (click)="filter.set(f.id)">{{ f.label }} <span class="count">{{ counts()[f.id] }}</span></button>
          }
        </div>
        @if (auth.isAdmin()) {
          <div class="segmented">
            <button [class.on]="scope() === 'mine'" (click)="setScope('mine')">Mine</button>
            <button [class.on]="scope() === 'all'" (click)="setScope('all')">All customers</button>
          </div>
        }
      </div>

      @if (loading() && orders().length === 0) {
        <div class="stack">
          @for (i of [1, 2, 3]; track i) { <div class="card skeleton" style="height: 120px"></div> }
        </div>
      } @else if (visible().length === 0) {
        <div class="card empty-state">
          <div class="empty-icon"><app-icon name="inbox" [size]="26" /></div>
          <h3>No orders {{ filter() === 'all' ? 'yet' : 'in this view' }}</h3>
          <p class="small">Place an order and it will appear here, updating in real time.</p>
          <a routerLink="/products" class="btn btn-primary">Start shopping</a>
        </div>
      } @else {
        <div class="stack">
          @for (o of visible(); track o.id) {
            <article class="card order" [class.flash]="flashed().has(o.id)" [class.open]="expanded() === o.id">
              <button class="summary" (click)="toggle(o.id)">
                <div class="id">
                  <span class="strong mono">{{ o.orderNumber }}</span>
                  <span class="xs subtle">{{ o.createdAtUtc | timeAgo: tick() }} · {{ o.items.length }} item{{ o.items.length === 1 ? '' : 's' }}@if (scope() === 'all') { · {{ o.customerEmail }} }</span>
                </div>

                <div class="pipeline hidden-sm">
                  @for (stage of pipeline; track stage.key; let i = $index; let last = $last) {
                    <div class="stage" [class]="'stage ' + stageState(o, i)">
                      <span class="stage-dot">
                        @if (stageState(o, i) === 'running') { <span class="spinner small-spin"></span> }
                        @else { <app-icon [name]="stageIcon(o, i, stage.icon)" [size]="14" [stroke]="2.4" /> }
                      </span>
                      <span class="xs">{{ i === 2 ? outcomeLabel(o) : stage.label }}</span>
                    </div>
                    @if (!last) { <span class="connector" [class.filled]="stageState(o, i + 1) === 'done' || stageState(o, i + 1) === 'running'" [class.failed]="stageState(o, i + 1) === 'failed'"></span> }
                  }
                </div>

                <div class="right">
                  <app-status-badge [status]="o.status" />
                  <span class="strong total">{{ o.total | currency }}</span>
                  <app-icon name="chevron-down" [size]="16" class="chev" />
                </div>
              </button>

              @if (expanded() === o.id) {
                <div class="details">
                  <div class="col">
                    <h4>Items</h4>
                    @for (item of o.items; track item.productId) {
                      <div class="row-between small item"><span>{{ item.quantity }} × {{ item.productName }}</span><span class="strong">{{ item.lineTotal | currency }}</span></div>
                    }
                    @if (o.discount > 0) {
                      <div class="row-between small item"><span class="muted">Subtotal</span><span>{{ o.subtotal | currency }}</span></div>
                      <div class="row-between small item discount"><span>Coupon <span class="mono">{{ o.couponCode }}</span></span><span>−{{ o.discount | currency }}</span></div>
                    }
                    <div class="row-between total-row"><span>Total</span><span>{{ o.total | currency }}</span></div>
                  </div>
                  <div class="col">
                    <h4>Shipping &amp; payment</h4>
                    <p class="small">{{ o.shippingAddress.fullName }}<br />{{ o.shippingAddress.line1 }}@if (o.shippingAddress.line2) {, {{ o.shippingAddress.line2 }}}<br />{{ o.shippingAddress.city }}, {{ o.shippingAddress.state }} {{ o.shippingAddress.postalCode }}<br />{{ o.shippingAddress.country }}</p>
                    <p class="small muted mt"><app-icon name="card" [size]="14" /> Card ending {{ o.cardLast4 }} · scenario <span class="badge">{{ o.paymentScenario }}</span></p>
                    @if (o.transactionReference) { <p class="xs muted">Transaction <span class="mono">{{ o.transactionReference }}</span></p> }
                    @if (o.failureReason) { <div class="alert alert-danger xs mt"><app-icon name="alert-circle" [size]="16" /> {{ o.failureReason }}</div> }
                  </div>
                  <div class="col">
                    <h4>Event timeline</h4>
                    <ol class="timeline">
                      @for (h of o.history; track h.occurredAtUtc + h.status) {
                        <li [class]="h.status">
                          <span class="t-dot"></span>
                          <div>
                            <div class="small strong">{{ h.note }}</div>
                            <div class="xs subtle">{{ h.occurredAtUtc | date: 'MMM d, HH:mm:ss' }}</div>
                          </div>
                        </li>
                      }
                    </ol>
                  </div>
                </div>
              }
            </article>
          }
        </div>
      }
    </div>
  `,
  styles: `
    .live {
      display: inline-flex; align-items: center; gap: 8px; font-size: 0.8rem; font-weight: 600; padding: 7px 12px; border-radius: 999px;
      background: var(--surface-3); color: var(--text-3);
      &.connected { background: var(--success-50); color: var(--success); }
      &.connecting, &.reconnecting { background: var(--warning-50); color: var(--warning); }
    }
    .toolbar { display: flex; justify-content: space-between; align-items: center; gap: 12px; flex-wrap: wrap; margin-bottom: 18px; }
    .tabs { display: flex; gap: 8px; flex-wrap: wrap; }
    .segmented { display: inline-flex; padding: 3px; border-radius: 10px; background: var(--surface-3); }
    .segmented button { border: 0; background: transparent; padding: 6px 12px; border-radius: 8px; font-weight: 600; font-size: 0.82rem; color: var(--text-2); cursor: pointer; }
    .segmented button.on { background: var(--surface); color: var(--text); box-shadow: var(--shadow-sm); }
    .order { overflow: hidden; transition: box-shadow 0.2s, border-color 0.4s; }
    .order.flash { animation: flash 1.6s ease; }
    @keyframes flash { 0%, 40% { border-color: var(--primary); box-shadow: 0 0 0 4px var(--primary-100); } }
    .summary { width: 100%; display: flex; align-items: center; gap: 20px; padding: 18px 20px; background: transparent; border: 0; cursor: pointer; text-align: left; color: var(--text); }
    .summary:hover { background: var(--surface-2); }
    .id { display: flex; flex-direction: column; gap: 2px; min-width: 190px; }
    .pipeline { flex: 1; display: flex; align-items: center; justify-content: center; gap: 8px; }
    .stage { display: flex; flex-direction: column; align-items: center; gap: 4px; color: var(--text-3); min-width: 78px; }
    .stage-dot { width: 30px; height: 30px; border-radius: 50%; display: grid; place-items: center; background: var(--surface-3); transition: all 0.3s; }
    .stage.done { color: var(--success); .stage-dot { background: var(--success); color: #fff; } }
    .stage.running { color: var(--info); .stage-dot { background: var(--info-50); color: var(--info); box-shadow: 0 0 0 4px var(--info-50); } }
    .stage.failed { color: var(--danger); .stage-dot { background: var(--danger); color: #fff; } }
    .small-spin { width: 14px; height: 14px; }
    .connector { flex: 0 1 60px; height: 2px; background: var(--border); border-radius: 2px; margin-bottom: 18px; transition: background 0.4s; }
    .connector.filled { background: var(--success); }
    .connector.failed { background: var(--danger); }
    .right { display: flex; align-items: center; gap: 16px; margin-left: auto; }
    .total { min-width: 84px; text-align: right; }
    .chev { color: var(--text-3); transition: transform 0.2s; }
    .open .chev { transform: rotate(180deg); }
    .details { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 24px; padding: 20px; border-top: 1px solid var(--border); background: var(--surface-2); animation: slide-up 0.2s var(--ease); }
    h4 { font-size: 0.75rem; text-transform: uppercase; letter-spacing: 0.06em; color: var(--text-3); margin-bottom: 10px; }
    .item { padding: 6px 0; border-bottom: 1px dashed var(--border); }
    .total-row { padding-top: 10px; font-weight: 700; }
    .discount { color: var(--success); font-weight: 600; }
    .mt { margin-top: 10px; }
    .timeline { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 14px; position: relative; }
    .timeline::before { content: ''; position: absolute; left: 5px; top: 6px; bottom: 6px; width: 2px; background: var(--border); }
    .timeline li { display: flex; gap: 12px; position: relative; }
    .t-dot { width: 12px; height: 12px; border-radius: 50%; background: var(--warning); margin-top: 4px; flex-shrink: 0; box-shadow: 0 0 0 3px var(--surface-2); z-index: 1; }
    .PaymentProcessing .t-dot { background: var(--info); }
    .Completed .t-dot { background: var(--success); }
    .Failed .t-dot { background: var(--danger); }
    @media (max-width: 860px) {
      .details { grid-template-columns: 1fr; }
      .summary { flex-wrap: wrap; gap: 12px; }
      .id { min-width: 0; }
    }
  `
})
export class OrdersPage implements OnInit {
  private readonly api = inject(OrdersApi);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly auth = inject(AuthService);
  protected readonly hub = inject(OrderHubService);

  readonly highlight = input<string>();

  protected readonly pipeline = PIPELINE;
  protected readonly filters: { id: Filter; label: string }[] = [
    { id: 'all', label: 'All' },
    { id: 'active', label: 'In progress' },
    { id: 'Completed', label: 'Completed' },
    { id: 'Failed', label: 'Failed' }
  ];

  protected readonly orders = signal<Order[]>([]);
  protected readonly loading = signal(true);
  protected readonly filter = signal<Filter>('all');
  protected readonly scope = signal<'mine' | 'all'>('mine');
  protected readonly expanded = signal<string | null>(null);
  protected readonly flashed = signal(new Set<string>());
  protected readonly tick = signal(0);

  protected readonly counts = computed(() => {
    const list = this.orders();
    return {
      all: list.length,
      active: list.filter(o => o.status === 'Pending' || o.status === 'PaymentProcessing').length,
      Completed: list.filter(o => o.status === 'Completed').length,
      Failed: list.filter(o => o.status === 'Failed').length
    } as Record<Filter, number>;
  });

  protected readonly visible = computed(() => {
    const f = this.filter();
    const list = this.orders();
    if (f === 'all') return list;
    if (f === 'active') return list.filter(o => o.status === 'Pending' || o.status === 'PaymentProcessing');
    return list.filter(o => o.status === f);
  });

  ngOnInit() {
    this.load();
    const h = this.highlight();
    if (h) {
      this.expanded.set(h);
      this.flash(h);
    }

    this.hub.orderUpdated$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(order => this.apply(order));

    const timer = setInterval(() => this.tick.update(t => t + 1), 15000);
    // Graceful degradation: if the real-time channel is down, poll while orders are still in flight.
    const fallback = setInterval(() => {
      if (this.hub.state() !== 'connected' && this.counts().active > 0 && document.visibilityState === 'visible') this.load();
    }, 6000);
    this.destroyRef.onDestroy(() => {
      clearInterval(timer);
      clearInterval(fallback);
    });
  }

  load() {
    this.loading.set(true);
    const source: Observable<Order[]> = this.scope() === 'all' ? this.api.all(100) : this.api.mine();
    source.subscribe({
      next: orders => {
        this.orders.set(orders);
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  setScope(scope: 'mine' | 'all') {
    this.scope.set(scope);
    this.load();
  }

  toggle(id: string) {
    this.expanded.update(current => (current === id ? null : id));
  }

  stageState(o: Order, index: number): 'done' | 'running' | 'failed' | 'idle' {
    const s: OrderStatus = o.status;
    if (index === 0) return 'done';
    if (index === 1) {
      if (s === 'Pending' || s === 'PaymentProcessing') return 'running';
      return s === 'Failed' ? 'failed' : 'done';
    }
    if (s === 'Completed') return 'done';
    if (s === 'Failed') return 'failed';
    return 'idle';
  }

  stageIcon(o: Order, index: number, fallback: string): string {
    const state = this.stageState(o, index);
    if (state === 'failed') return 'x';
    if (state === 'done') return 'check';
    return fallback;
  }

  outcomeLabel(o: Order): string {
    return o.status === 'Completed' ? 'Completed' : o.status === 'Failed' ? 'Failed' : 'Outcome';
  }

  private apply(order: Order) {
    const isMine = order.userId === this.auth.user()?.id;
    if (this.scope() === 'mine' && !isMine) return;

    const existing = this.orders().find(o => o.id === order.id);
    if (existing && new Date(existing.updatedAtUtc) > new Date(order.updatedAtUtc)) return;

    this.orders.update(list => (existing ? list.map(o => (o.id === order.id ? order : o)) : [order, ...list]));
    this.flash(order.id);

    if (isMine && existing?.status !== order.status) {
      if (order.status === 'Completed') this.toast.success(`Order ${order.orderNumber} completed`, 'Payment captured — confirmation email sent.');
      if (order.status === 'Failed') this.toast.error(`Order ${order.orderNumber} failed`, order.failureReason ?? undefined);
    }
  }

  private flash(id: string) {
    this.flashed.update(s => new Set(s).add(id));
    setTimeout(() => this.flashed.update(s => {
      const next = new Set(s);
      next.delete(id);
      return next;
    }), 1700);
  }
}
