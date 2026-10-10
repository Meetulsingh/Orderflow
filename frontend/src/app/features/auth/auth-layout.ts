import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { WarmupService } from '../../core/warmup.service';
import { Icon } from '../../shared/icon';

/** Split-screen frame for sign-in / sign-up with an architecture showcase panel. */
@Component({
  selector: 'app-auth-layout',
  imports: [Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="wrap">
      <section class="brand-panel">
        <div class="brand">
          <img src="favicon.svg" alt="" width="36" height="36" />
          <span>OrderFlow</span>
        </div>

        <div class="pitch">
          <span class="pill"><app-icon name="sparkles" [size]="14" /> Event-driven commerce platform</span>
          <h1>Orders that flow through a resilient, real-time microservice pipeline.</h1>
          <p>Place an order and watch it travel from the API gateway through RabbitMQ to independent payment and notification workers, with status pushed live over SignalR.</p>
        </div>

        <div class="flow">
          @for (step of steps; track step.label; let last = $last) {
            <div class="node">
              <span class="node-icon"><app-icon [name]="step.icon" [size]="16" /></span>
              <span>{{ step.label }}</span>
            </div>
            @if (!last) { <span class="arrow"><app-icon name="chevron-right" [size]="14" /></span> }
          }
        </div>

        <ul class="features">
          <li><app-icon name="layers" [size]="16" /> CQRS with MediatR &amp; transactional outbox</li>
          <li><app-icon name="zap" [size]="16" /> Redis cache-aside catalog</li>
          <li><app-icon name="shield" [size]="16" /> JWT + rotating refresh tokens, rate limiting</li>
          <li><app-icon name="alert" [size]="16" /> Retries &amp; dead-letter queues</li>
        </ul>
      </section>

      <section class="form-panel">
        <div class="form-inner">
          <ng-content />
          @if (warmup.phase() === 'waking') {
            <div class="alert alert-info waking">
              <span class="spinner"></span>
              <span>Waking up backend services ({{ warmup.readyCount() }}/{{ warmup.total() }} ready). Free-tier hosting sleeps when idle, so the first request can take up to a minute.</span>
            </div>
          }
        </div>
      </section>
    </div>
  `,
  styles: `
    .wrap { display: grid; grid-template-columns: minmax(0, 1.05fr) minmax(0, 1fr); min-height: 100vh; }
    .brand-panel {
      position: relative; overflow: hidden; color: #e0e7ff; padding: 40px 56px;
      display: flex; flex-direction: column; gap: 40px;
      background:
        radial-gradient(circle at 15% 10%, rgba(129,140,248,0.45), transparent 45%),
        radial-gradient(circle at 90% 80%, rgba(168,85,247,0.4), transparent 50%),
        linear-gradient(160deg, #1e1b4b 0%, #312e81 55%, #4c1d95 100%);
    }
    .brand-panel::after {
      content: ''; position: absolute; inset: 0; pointer-events: none;
      background-image: linear-gradient(rgba(255,255,255,0.05) 1px, transparent 1px), linear-gradient(90deg, rgba(255,255,255,0.05) 1px, transparent 1px);
      background-size: 36px 36px; mask-image: radial-gradient(ellipse at center, black 30%, transparent 75%);
    }
    .brand { display: flex; align-items: center; gap: 12px; font-weight: 750; font-size: 1.2rem; color: #fff; letter-spacing: -0.03em; }
    .pitch { margin-top: auto; max-width: 540px; position: relative; z-index: 1; }
    .pill {
      display: inline-flex; align-items: center; gap: 6px; padding: 6px 12px; border-radius: 999px;
      background: rgba(255,255,255,0.1); border: 1px solid rgba(255,255,255,0.15); font-size: 0.8rem; font-weight: 600;
    }
    h1 { color: #fff; font-size: 2.3rem; line-height: 1.12; letter-spacing: -0.035em; margin: 18px 0 14px; font-weight: 750; }
    .pitch p { color: #c7d2fe; font-size: 1rem; line-height: 1.6; }
    .flow { display: flex; align-items: center; gap: 6px; flex-wrap: wrap; position: relative; z-index: 1; }
    .node {
      display: inline-flex; align-items: center; gap: 8px; padding: 8px 12px; border-radius: 12px;
      background: rgba(255,255,255,0.08); border: 1px solid rgba(255,255,255,0.14); font-size: 0.8rem; font-weight: 600; color: #fff;
    }
    .node-icon { color: #a5b4fc; display: inline-flex; }
    .arrow { color: #818cf8; display: inline-flex; }
    .features { list-style: none; padding: 0; margin: 0 0 auto; display: grid; grid-template-columns: 1fr 1fr; gap: 12px 20px; position: relative; z-index: 1; }
    .features li { display: flex; align-items: center; gap: 10px; font-size: 0.86rem; color: #c7d2fe; }
    .features app-icon { color: #a5b4fc; }
    .form-panel { display: grid; place-items: center; padding: 40px 24px; background: var(--bg-elevated); }
    .form-inner { width: min(420px, 100%); display: flex; flex-direction: column; gap: 20px; }
    .waking { align-items: center; }
    @media (max-width: 960px) {
      .wrap { grid-template-columns: 1fr; }
      .brand-panel { padding: 28px 24px; gap: 24px; }
      .pitch { margin-top: 0; }
      h1 { font-size: 1.6rem; }
      .features { display: none; }
    }
  `
})
export class AuthLayout {
  protected readonly warmup = inject(WarmupService);
  protected readonly steps = [
    { icon: 'shield', label: 'Gateway' },
    { icon: 'database', label: 'Ordering' },
    { icon: 'radio', label: 'RabbitMQ' },
    { icon: 'card', label: 'Payment' },
    { icon: 'mail', label: 'Notify' }
  ];
}
