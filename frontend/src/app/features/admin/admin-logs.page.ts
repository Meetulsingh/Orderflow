import { CurrencyPipe, DatePipe, DecimalPipe, JsonPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { AdminApi, OrdersApi, SystemApi } from '../../core/api.service';
import {
  DeadLetter, EventLog, EventStats, NotificationMessage, OrderStats, PaymentRecord, Severity, SystemHealth
} from '../../core/models';
import { ToastService } from '../../core/toast.service';
import { Icon } from '../../shared/icon';
import { TimeAgoPipe } from '../../shared/time-ago.pipe';

type Tab = 'events' | 'emails' | 'payments';

const SERVICE_META: Record<string, { label: string; icon: string; tech: string }> = {
  gateway: { label: 'API Gateway + Identity', icon: 'shield', tech: 'YARP · JWT issuance · rate limiting' },
  catalog: { label: 'Catalog', icon: 'package', tech: 'PostgreSQL · Redis cache' },
  ordering: { label: 'Ordering', icon: 'layers', tech: 'CQRS · outbox · SignalR' },
  payment: { label: 'Payment Worker', icon: 'card', tech: 'RabbitMQ consumer' },
  notification: { label: 'Notification Worker', icon: 'mail', tech: 'RabbitMQ consumer' }
};

@Component({
  selector: 'app-admin-logs-page',
  imports: [CurrencyPipe, DatePipe, DecimalPipe, JsonPipe, Icon, TimeAgoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page">
      <div class="page-header">
        <div>
          <span class="eyebrow"><app-icon name="server" [size]="14" /> Admin · Observability</span>
          <h1>System monitor</h1>
          <p class="subtitle">Live health of every microservice, the integration event stream flowing through RabbitMQ, and dead-letter queue alerts.</p>
        </div>
        <div class="row">
          <label class="toggle">
            <input type="checkbox" [checked]="autoRefresh()" (change)="autoRefresh.set(!autoRefresh())" />
            <span class="track"><span class="thumb"></span></span>
            <span class="small">Auto-refresh 5s</span>
          </label>
          <button class="btn btn-sm" (click)="refresh()" [disabled]="refreshing()">
            @if (refreshing()) { <span class="spinner small-spin"></span> } @else { <app-icon name="refresh" [size]="14" /> } Refresh
          </button>
          <button class="btn btn-sm btn-primary" (click)="sendDeals()" [disabled]="sendingDeals()"
            title="Starts a new weekly deals campaign now: a coupon for 3 products, emailed to every customer">
            @if (sendingDeals()) { <span class="spinner small-spin"></span> } @else { <app-icon name="mail" [size]="14" /> } Send weekly deals now
          </button>
        </div>
      </div>

      <!-- KPI tiles -->
      <section class="kpis">
        <div class="card kpi">
          <span class="k-icon primary"><app-icon name="package" /></span>
          <div><div class="k-label">Total orders</div><div class="k-value">{{ stats()?.totalOrders ?? '—' }}</div><div class="xs subtle">{{ stats()?.ordersLast24h ?? 0 }} in last 24h</div></div>
        </div>
        <div class="card kpi">
          <span class="k-icon success"><app-icon name="dollar" /></span>
          <div><div class="k-label">Revenue</div><div class="k-value">{{ (stats()?.revenue ?? 0) | currency: 'USD' : 'symbol' : '1.0-0' }}</div><div class="xs subtle">AOV {{ (stats()?.averageOrderValue ?? 0) | currency }}</div></div>
        </div>
        <div class="card kpi">
          <span class="k-icon info"><app-icon name="check-circle" /></span>
          <div><div class="k-label">Success rate</div><div class="k-value">{{ successRate() | number: '1.0-0' }}%</div><div class="xs subtle">{{ stats()?.completed ?? 0 }} completed · {{ stats()?.failed ?? 0 }} failed</div></div>
        </div>
        <div class="card kpi">
          <span class="k-icon warning"><app-icon name="radio" /></span>
          <div><div class="k-label">Events (24h)</div><div class="k-value">{{ eventStats()?.totalEvents24h ?? '—' }}</div><div class="xs subtle">{{ eventStats()?.emailsSent24h ?? 0 }} emails sent</div></div>
        </div>
        <div class="card kpi" [class.alarm]="(eventStats()?.deadLetters ?? 0) > 0">
          <span class="k-icon danger"><app-icon name="alert" /></span>
          <div><div class="k-label">Dead letters</div><div class="k-value">{{ eventStats()?.deadLetters ?? '—' }}</div><div class="xs subtle">messages in error queues</div></div>
        </div>
      </section>

      <!-- Service health -->
      <section class="card section">
        <div class="card-header">
          <div class="row">
            <h2>Service health</h2>
            @if (health(); as h) {
              <span class="badge" [class.badge-success]="h.status === 'Healthy'" [class.badge-warning]="h.status === 'Degraded'" [class.badge-danger]="h.status === 'Unhealthy'">
                <span class="dot"></span>{{ h.status }}
              </span>
            }
          </div>
          @if (health(); as h) { <span class="xs subtle">Checked {{ h.checkedAtUtc | timeAgo: tick() }}</span> }
        </div>
        <div class="services">
          @for (s of health()?.services ?? []; track s.name) {
            <div class="svc" [class]="'svc ' + s.status.toLowerCase()">
              <div class="row-between">
                <div class="row">
                  <span class="svc-icon"><app-icon [name]="meta(s.name).icon" [size]="17" /></span>
                  <div>
                    <div class="strong small">{{ meta(s.name).label }}</div>
                    <div class="xs subtle">{{ meta(s.name).tech }}</div>
                  </div>
                </div>
                <span class="status-pill"><span class="dot" [class.pulse]="s.status === 'Healthy'"></span>{{ s.status }}</span>
              </div>
              <div class="checks">
                @if (s.latencyMs !== null && s.latencyMs !== undefined) { <span class="check mono">{{ s.latencyMs }} ms</span> }
                @for (c of s.details?.checks ?? []; track c.name) {
                  @if (c.name !== 'self') {
                    <span class="check" [class.bad]="c.status !== 'Healthy'" [title]="c.description || c.status">
                      <app-icon [name]="checkIcon(c.name)" [size]="12" /> {{ checkLabel(c.name) }}
                    </span>
                  }
                }
              </div>
            </div>
          } @empty {
            @for (i of [1, 2, 3, 4, 5, 6]; track i) { <div class="svc skeleton" style="height: 92px"></div> }
          }
        </div>
      </section>

      <!-- Dead letters -->
      @if (deadLetters().length > 0) {
        <section class="card section dlq">
          <div class="card-header">
            <div class="row"><app-icon name="alert" class="danger-text" /><h2>Dead-letter queue alerts</h2></div>
            <span class="badge badge-danger">{{ deadLetters().length }}</span>
          </div>
          <div class="table-wrap">
            <table class="table">
              <thead><tr><th>Message</th><th>Order</th><th>Exception</th><th>When</th></tr></thead>
              <tbody>
                @for (d of deadLetters(); track d.id) {
                  <tr>
                    <td><span class="badge badge-danger">{{ d.messageType }}</span></td>
                    <td class="mono">{{ d.orderNumber ?? '—' }}</td>
                    <td><div class="small strong">{{ d.exceptionType.split('.').pop() }}</div><div class="xs muted">{{ d.exceptionMessage }}</div></td>
                    <td class="xs subtle nowrap">{{ d.faultedAtUtc | timeAgo: tick() }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </section>
      }

      <!-- Streams -->
      <section class="card section">
        <div class="card-header tabs-header">
          <div class="tabs">
            <button [class.on]="tab() === 'events'" (click)="tab.set('events')"><app-icon name="radio" [size]="15" /> Event stream</button>
            <button [class.on]="tab() === 'emails'" (click)="tab.set('emails')"><app-icon name="mail" [size]="15" /> Emails <span class="count">{{ emails().length }}</span></button>
            <button [class.on]="tab() === 'payments'" (click)="tab.set('payments')"><app-icon name="card" [size]="15" /> Payments <span class="count">{{ payments().length }}</span></button>
          </div>
          @if (tab() === 'events') {
            <div class="sev-filter">
              @for (s of severities; track s) {
                <button class="chip" [class.active]="severity() === s" (click)="setSeverity(s)">{{ s || 'All' }}</button>
              }
            </div>
          }
        </div>

        @switch (tab()) {
          @case ('events') {
            @if (events().length === 0) {
              <div class="empty-state"><div class="empty-icon"><app-icon name="radio" [size]="24" /></div><h3>No events yet</h3><p class="small">Place an order to see events flow through the bus.</p></div>
            } @else {
              <ul class="events">
                @for (e of events(); track e.id) {
                  <li [class]="'ev ' + e.severity.toLowerCase()" (click)="openEvent.set(openEvent() === e.id ? null : e.id)">
                    <span class="ev-icon"><app-icon [name]="severityIcon(e.severity)" [size]="15" /></span>
                    <div class="grow ev-body">
                      <div class="row wrap ev-head">
                        <span class="mono strong ev-type">{{ e.eventType }}</span>
                        <span class="badge badge-outline">{{ e.source }}</span>
                        @if (e.orderNumber) { <span class="mono xs subtle">{{ e.orderNumber }}</span> }
                      </div>
                      <div class="small muted">{{ e.summary }}</div>
                      @if (openEvent() === e.id) {
                        <pre class="payload">{{ parse(e.payload) | json }}</pre>
                      }
                    </div>
                    <span class="xs subtle nowrap" [title]="e.occurredAtUtc | date: 'medium'">{{ e.occurredAtUtc | timeAgo: tick() }}</span>
                  </li>
                }
              </ul>
            }
          }
          @case ('emails') {
            @if (emails().length === 0) {
              <div class="empty-state"><div class="empty-icon"><app-icon name="mail" [size]="24" /></div><h3>No emails sent yet</h3></div>
            } @else {
              <ul class="emails">
                @for (m of emails(); track m.id) {
                  <li>
                    <span class="avatar"><app-icon name="mail" [size]="15" /></span>
                    <div class="grow">
                      <div class="row-between"><span class="strong small">{{ m.subject }}</span><span class="xs subtle nowrap">{{ m.sentAtUtc | timeAgo: tick() }}</span></div>
                      <div class="xs subtle">to {{ m.recipient }}</div>
                      <p class="xs muted body-text">{{ m.body }}</p>
                    </div>
                  </li>
                }
              </ul>
            }
          }
          @case ('payments') {
            <div class="table-wrap">
              <table class="table">
                <thead><tr><th>Order</th><th>Customer</th><th>Card</th><th class="right">Amount</th><th>Status</th><th>Reference</th><th>When</th></tr></thead>
                <tbody>
                  @for (p of payments(); track p.id) {
                    <tr>
                      <td class="mono">{{ p.orderNumber }}</td>
                      <td class="small">{{ p.customerEmail }}</td>
                      <td class="mono">•••• {{ p.cardLast4 }}</td>
                      <td class="right strong">{{ p.amount | currency }}</td>
                      <td><span class="badge" [class.badge-success]="p.status === 'Succeeded'" [class.badge-warning]="p.status === 'Declined'">{{ p.status }}</span></td>
                      <td class="mono xs">{{ p.transactionReference ?? p.failureReason }}</td>
                      <td class="xs subtle nowrap">{{ p.processedAtUtc | timeAgo: tick() }}</td>
                    </tr>
                  } @empty {
                    <tr><td colspan="7" class="center muted small">No payments processed yet.</td></tr>
                  }
                </tbody>
              </table>
            </div>
          }
        }
      </section>
    </div>
  `,
  styles: `
    .toggle { display: inline-flex; align-items: center; gap: 8px; cursor: pointer; user-select: none; }
    .toggle input { display: none; }
    .track { width: 34px; height: 20px; border-radius: 999px; background: var(--border-strong); position: relative; transition: background 0.2s; }
    .thumb { position: absolute; top: 2px; left: 2px; width: 16px; height: 16px; border-radius: 50%; background: #fff; box-shadow: var(--shadow-sm); transition: transform 0.2s var(--ease); }
    .toggle input:checked + .track { background: var(--primary); }
    .toggle input:checked + .track .thumb { transform: translateX(14px); }
    .small-spin { width: 14px; height: 14px; }
    .kpis { display: grid; grid-template-columns: repeat(5, minmax(0, 1fr)); gap: 16px; margin-bottom: 20px; }
    .kpi { display: flex; gap: 14px; padding: 18px; align-items: flex-start; }
    .kpi.alarm { border-color: #fecdd3; background: linear-gradient(180deg, #fff, var(--danger-50)); }
    .k-icon { width: 40px; height: 40px; border-radius: 12px; display: grid; place-items: center; flex-shrink: 0; }
    .k-icon.primary { background: var(--primary-50); color: var(--primary); }
    .k-icon.success { background: var(--success-50); color: var(--success); }
    .k-icon.info { background: var(--info-50); color: var(--info); }
    .k-icon.warning { background: var(--warning-50); color: var(--warning); }
    .k-icon.danger { background: var(--danger-50); color: var(--danger); }
    .k-label { font-size: 0.78rem; color: var(--text-2); font-weight: 550; }
    .k-value { font-size: 1.55rem; font-weight: 750; letter-spacing: -0.03em; line-height: 1.2; margin: 2px 0; }
    .section { margin-bottom: 20px; overflow: hidden; }
    .services { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 14px; padding: 18px; }
    .svc { border: 1px solid var(--border); border-radius: 14px; padding: 14px; display: flex; flex-direction: column; gap: 12px; background: var(--surface); }
    .svc-icon { width: 34px; height: 34px; border-radius: 10px; display: grid; place-items: center; background: var(--surface-3); color: var(--text-2); }
    .status-pill { display: inline-flex; align-items: center; gap: 6px; font-size: 0.74rem; font-weight: 650; }
    .svc.healthy { .status-pill { color: var(--success); } .svc-icon { background: var(--success-50); color: var(--success); } }
    .svc.degraded { border-color: #fde68a; .status-pill { color: var(--warning); } .svc-icon { background: var(--warning-50); color: var(--warning); } }
    .svc.unhealthy, .svc.unreachable { border-color: #fecdd3; .status-pill { color: var(--danger); } .svc-icon { background: var(--danger-50); color: var(--danger); } }
    .checks { display: flex; flex-wrap: wrap; gap: 6px; }
    .check { display: inline-flex; align-items: center; gap: 4px; font-size: 0.72rem; padding: 3px 8px; border-radius: 6px; background: var(--success-50); color: var(--success); font-weight: 600; }
    .check.mono { background: var(--surface-3); color: var(--text-2); }
    .check.bad { background: var(--danger-50); color: var(--danger); }
    .dlq { border-color: #fecdd3; }
    .dlq .card-header { background: var(--danger-50); }
    .danger-text { color: var(--danger); }
    .nowrap { white-space: nowrap; }
    .center { text-align: center; }
    .tabs-header { flex-wrap: wrap; padding: 10px 14px; }
    .tabs { display: flex; gap: 4px; }
    .tabs button {
      display: inline-flex; align-items: center; gap: 7px; border: 0; background: transparent; padding: 8px 12px; border-radius: 9px;
      font-weight: 600; font-size: 0.86rem; color: var(--text-2); cursor: pointer;
      &:hover { background: var(--surface-3); }
      &.on { background: var(--primary-50); color: var(--primary); }
    }
    .count { font-size: 0.7rem; padding: 1px 7px; border-radius: 999px; background: var(--surface-3); }
    .sev-filter { display: flex; gap: 6px; flex-wrap: wrap; }
    .sev-filter .chip { height: 28px; font-size: 0.78rem; padding: 0 11px; }
    .events, .emails { list-style: none; margin: 0; padding: 0; max-height: 560px; overflow-y: auto; }
    .ev { display: flex; gap: 14px; align-items: flex-start; padding: 14px 20px; border-bottom: 1px solid var(--border); cursor: pointer; transition: background 0.12s; animation: slide-up 0.25s var(--ease); }
    .ev:hover { background: var(--surface-2); }
    .ev-icon { width: 30px; height: 30px; border-radius: 9px; display: grid; place-items: center; flex-shrink: 0; }
    .ev.info .ev-icon { background: var(--info-50); color: var(--info); }
    .ev.success .ev-icon { background: var(--success-50); color: var(--success); }
    .ev.warning .ev-icon { background: var(--warning-50); color: var(--warning); }
    .ev.error .ev-icon { background: var(--danger-50); color: var(--danger); }
    .ev.error { background: linear-gradient(90deg, var(--danger-50), transparent 60%); }
    .ev-body { min-width: 0; }
    .ev-head { gap: 8px; margin-bottom: 2px; }
    .ev-type { font-size: 0.84rem; }
    .payload { margin: 10px 0 0; padding: 12px; border-radius: 10px; background: #0f172a; color: #c7d2fe; font-family: var(--mono); font-size: 0.74rem; overflow-x: auto; max-height: 260px; }
    .emails li { display: flex; gap: 12px; padding: 14px 20px; border-bottom: 1px solid var(--border); }
    .avatar { width: 32px; height: 32px; border-radius: 50%; display: grid; place-items: center; background: var(--primary-50); color: var(--primary); flex-shrink: 0; }
    .body-text { white-space: pre-line; margin-top: 6px; }
    @media (max-width: 1100px) { .kpis { grid-template-columns: repeat(3, minmax(0, 1fr)); } .services { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
    @media (max-width: 640px) { .kpis { grid-template-columns: 1fr 1fr; } .services { grid-template-columns: 1fr; } }
  `
})
export class AdminLogsPage implements OnInit {
  private readonly admin = inject(AdminApi);
  private readonly ordersApi = inject(OrdersApi);
  private readonly system = inject(SystemApi);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly severities: (Severity | '')[] = ['', 'Info', 'Success', 'Warning', 'Error'];

  protected readonly health = signal<SystemHealth | null>(null);
  protected readonly stats = signal<OrderStats | null>(null);
  protected readonly eventStats = signal<EventStats | null>(null);
  protected readonly events = signal<EventLog[]>([]);
  protected readonly deadLetters = signal<DeadLetter[]>([]);
  protected readonly emails = signal<NotificationMessage[]>([]);
  protected readonly payments = signal<PaymentRecord[]>([]);

  protected readonly tab = signal<Tab>('events');
  protected readonly severity = signal<Severity | ''>('');
  protected readonly openEvent = signal<string | null>(null);
  protected readonly autoRefresh = signal(true);
  protected readonly refreshing = signal(false);
  protected readonly sendingDeals = signal(false);
  protected readonly tick = signal(0);
  private readonly toast = inject(ToastService);

  protected readonly successRate = computed(() => {
    const s = this.stats();
    const decided = (s?.completed ?? 0) + (s?.failed ?? 0);
    return decided === 0 ? 0 : ((s?.completed ?? 0) / decided) * 100;
  });

  ngOnInit() {
    this.refresh();
    let n = 0;
    const timer = setInterval(() => {
      this.tick.update(t => t + 1);
      if (!this.autoRefresh() || document.visibilityState !== 'visible') return;
      n++;
      this.refreshFast();
      if (n % 3 === 0) this.loadHealth(); // health probes are heavier; refresh every 15s
    }, 5000);
    this.destroyRef.onDestroy(() => clearInterval(timer));
  }

  refresh() {
    this.refreshing.set(true);
    this.loadHealth();
    this.refreshFast(() => this.refreshing.set(false));
  }

  sendDeals() {
    this.sendingDeals.set(true);
    this.admin.sendWeeklyDeals().subscribe({
      next: () => {
        this.sendingDeals.set(false);
        this.toast.success('Weekly deals campaign started',
          'A coupon is being created and emailed to customers. Watch the Event stream and Emails tabs.');
        this.tab.set('emails');
      },
      error: () => this.sendingDeals.set(false)
    });
  }

  setSeverity(s: Severity | '') {
    this.severity.set(s);
    this.admin.events(60, s || undefined).pipe(catchError(() => of(null))).subscribe(e => e && this.events.set(e));
  }

  meta(name: string) {
    return SERVICE_META[name] ?? { label: name, icon: 'server', tech: '' };
  }

  checkIcon(name: string): string {
    return name === 'postgres' ? 'database' : name === 'redis' ? 'zap' : name.includes('masstransit') ? 'radio' : 'check';
  }

  checkLabel(name: string): string {
    return name === 'postgres' ? 'PostgreSQL' : name === 'redis' ? 'Redis' : name.includes('masstransit') ? 'RabbitMQ' : name;
  }

  severityIcon(s: Severity): string {
    return s === 'Error' ? 'skull' : s === 'Warning' ? 'alert' : s === 'Success' ? 'check-circle' : 'radio';
  }

  parse(payload: string): unknown {
    try {
      return JSON.parse(payload);
    } catch {
      return payload;
    }
  }

  private loadHealth() {
    this.system.health().pipe(catchError(() => of(null))).subscribe(h => h && this.health.set(h));
  }

  private refreshFast(done?: () => void) {
    const safe = <T>(o: import('rxjs').Observable<T>) => o.pipe(catchError(() => of(null)));
    forkJoin({
      stats: safe(this.ordersApi.stats()),
      eventStats: safe(this.admin.eventStats()),
      events: safe(this.admin.events(60, this.severity() || undefined)),
      deadLetters: safe(this.admin.deadLetters()),
      emails: safe(this.admin.notifications()),
      payments: safe(this.admin.payments())
    }).subscribe(r => {
      if (r.stats) this.stats.set(r.stats);
      if (r.eventStats) this.eventStats.set(r.eventStats);
      if (r.events) this.events.set(r.events);
      if (r.deadLetters) this.deadLetters.set(r.deadLetters);
      if (r.emails) this.emails.set(r.emails);
      if (r.payments) this.payments.set(r.payments);
      done?.();
    });
  }
}
