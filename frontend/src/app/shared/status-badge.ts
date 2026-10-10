import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { OrderStatus } from '../core/models';

const MAP: Record<OrderStatus, { label: string; cls: string; pulse: boolean }> = {
  Pending: { label: 'Pending', cls: 'badge-warning', pulse: true },
  PaymentProcessing: { label: 'Processing payment', cls: 'badge-info', pulse: true },
  Completed: { label: 'Completed', cls: 'badge-success', pulse: false },
  Failed: { label: 'Failed', cls: 'badge-danger', pulse: false }
};

@Component({
  selector: 'app-status-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="badge" [class]="'badge ' + meta().cls"><span class="dot" [class.pulse]="meta().pulse"></span>{{ meta().label }}</span>`
})
export class StatusBadge {
  readonly status = input.required<OrderStatus>();
  protected readonly meta = computed(() => MAP[this.status()] ?? MAP.Pending);
}
