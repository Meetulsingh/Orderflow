import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { OrdersApi } from '../../core/api.service';
import { AuthService } from '../../core/auth/auth.service';
import { CartStore } from '../../core/cart.store';
import { problemMessage } from '../../core/error.interceptor';
import { PaymentScenario } from '../../core/models';
import { ToastService } from '../../core/toast.service';
import { Icon } from '../../shared/icon';
import { ProductArt } from '../../shared/product-art';

type Step = 0 | 1 | 2;

const SCENARIOS: { id: PaymentScenario; title: string; desc: string; icon: string; tone: string }[] = [
  { id: 'approve', title: 'Approve', desc: 'Payment worker captures the charge → order Completed, stock deducted, confirmation email.', icon: 'check-circle', tone: 'success' },
  { id: 'decline', title: 'Decline', desc: 'Issuer declines the card → PaymentFailed event → order Failed, no stock change.', icon: 'x-circle', tone: 'warning' },
  { id: 'chaos', title: 'Chaos → DLQ', desc: 'Payment worker crashes on every attempt → retries exhausted → dead-letter queue → compensation.', icon: 'skull', tone: 'danger' }
];

@Component({
  selector: 'app-checkout-page',
  imports: [ReactiveFormsModule, RouterLink, CurrencyPipe, Icon, ProductArt],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page">
      <div class="page-header">
        <div>
          <span class="eyebrow"><app-icon name="lock" [size]="13" /> Secure checkout</span>
          <h1>Checkout</h1>
        </div>
        <a routerLink="/products" class="btn btn-ghost"><app-icon name="arrow-left" [size]="16" /> Continue shopping</a>
      </div>

      @if (cart.isEmpty()) {
        <div class="card empty-state">
          <div class="empty-icon"><app-icon name="cart" [size]="26" /></div>
          <h3>Your cart is empty</h3>
          <p class="small">Add some products before checking out.</p>
          <a routerLink="/products" class="btn btn-primary">Browse the catalog</a>
        </div>
      } @else {
        <ol class="stepper">
          @for (s of stepLabels; track s.label; let i = $index) {
            <li [class.active]="step() === i" [class.done]="step() > i">
              <button type="button" (click)="goTo($any(i))" [disabled]="i > maxReached()">
                <span class="num">@if (step() > i) { <app-icon name="check" [size]="14" [stroke]="3" /> } @else { {{ i + 1 }} }</span>
                <span class="lbl"><span class="strong">{{ s.label }}</span><span class="xs subtle hidden-sm">{{ s.hint }}</span></span>
              </button>
            </li>
          }
        </ol>

        <div class="layout">
          <div class="main">
            @switch (step()) {
              @case (0) {
                <section class="card fade-up">
                  <div class="card-header"><h2>Review your cart</h2><span class="badge">{{ cart.count() }} items</span></div>
                  <div class="lines">
                    @for (line of cart.lines(); track line.productId) {
                      <div class="line">
                        <div class="thumb"><app-product-art [name]="line.name" [category]="line.category" [iconSize]="24" /></div>
                        <div class="grow">
                          <div class="strong">{{ line.name }}</div>
                          <div class="xs subtle">{{ line.category }} · {{ line.price | currency }} each</div>
                        </div>
                        <div class="qty">
                          <button class="btn btn-sm btn-icon" (click)="cart.setQuantity(line.productId, line.quantity - 1)" aria-label="Decrease"><app-icon name="minus" [size]="14" /></button>
                          <span>{{ line.quantity }}</span>
                          <button class="btn btn-sm btn-icon" (click)="cart.setQuantity(line.productId, line.quantity + 1)" [disabled]="line.quantity >= line.stock" aria-label="Increase"><app-icon name="plus" [size]="14" /></button>
                        </div>
                        <div class="amount strong">{{ line.price * line.quantity | currency }}</div>
                        <button class="btn btn-ghost btn-sm btn-icon" (click)="cart.remove(line.productId)" aria-label="Remove"><app-icon name="trash" [size]="15" /></button>
                      </div>
                    }
                  </div>
                  <div class="card-pad nav-row">
                    <span></span>
                    <button class="btn btn-primary" (click)="advance(1)">Continue to shipping <app-icon name="arrow-right" [size]="16" /></button>
                  </div>
                </section>
              }
              @case (1) {
                <section class="card fade-up">
                  <div class="card-header"><h2>Shipping address</h2><app-icon name="truck" class="subtle" /></div>
                  <form [formGroup]="shipping" class="card-pad" (ngSubmit)="submitShipping()" novalidate>
                    <div class="form-grid">
                      <div class="field span-2">
                        <label for="fullName">Full name</label>
                        <input id="fullName" class="input" formControlName="fullName" autocomplete="name" />
                      </div>
                      <div class="field span-2">
                        <label for="line1">Address line 1</label>
                        <input id="line1" class="input" formControlName="line1" autocomplete="address-line1" placeholder="Street address" />
                      </div>
                      <div class="field span-2">
                        <label for="line2">Address line 2 <span class="subtle">(optional)</span></label>
                        <input id="line2" class="input" formControlName="line2" autocomplete="address-line2" placeholder="Apartment, suite, etc." />
                      </div>
                      <div class="field">
                        <label for="city">City</label>
                        <input id="city" class="input" formControlName="city" autocomplete="address-level2" />
                      </div>
                      <div class="field">
                        <label for="state">State / Province</label>
                        <input id="state" class="input" formControlName="state" autocomplete="address-level1" />
                      </div>
                      <div class="field">
                        <label for="postalCode">Postal code</label>
                        <input id="postalCode" class="input" formControlName="postalCode" autocomplete="postal-code" />
                      </div>
                      <div class="field">
                        <label for="country">Country</label>
                        <select id="country" class="select" formControlName="country" autocomplete="country-name">
                          @for (c of countries; track c) { <option [value]="c">{{ c }}</option> }
                        </select>
                      </div>
                    </div>
                    @if (shipping.invalid && shipping.touched) {
                      <div class="alert alert-warning mt"><app-icon name="alert" /> Please complete all required fields.</div>
                    }
                    <div class="nav-row mt">
                      <button type="button" class="btn btn-ghost" (click)="goTo(0)"><app-icon name="arrow-left" [size]="16" /> Back</button>
                      <button type="submit" class="btn btn-primary">Continue to payment <app-icon name="arrow-right" [size]="16" /></button>
                    </div>
                  </form>
                </section>
              }
              @case (2) {
                <section class="card fade-up">
                  <div class="card-header"><h2>Payment</h2><span class="badge badge-primary"><app-icon name="lock" [size]="12" /> Simulated</span></div>
                  <div class="card-pad stack">
                    <div class="credit-card">
                      <div class="row-between"><app-icon name="card" [size]="26" /><span class="xs">OrderFlow Pay</span></div>
                      <div class="cc-number mono">{{ maskedCard() }}</div>
                      <div class="row-between xs"><span>{{ payment.controls.cardName.value || 'CARDHOLDER' }}</span><span>{{ payment.controls.expiry.value || 'MM/YY' }}</span></div>
                    </div>

                    <form [formGroup]="payment" class="form-grid" novalidate>
                      <div class="field span-2">
                        <label for="cardNumber">Card number</label>
                        <input id="cardNumber" class="input mono" formControlName="cardNumber" inputmode="numeric" maxlength="19" (input)="formatCard()" />
                        <span class="hint">Test card — only the last 4 digits ever leave your browser.</span>
                      </div>
                      <div class="field span-2">
                        <label for="cardName">Name on card</label>
                        <input id="cardName" class="input" formControlName="cardName" />
                      </div>
                      <div class="field">
                        <label for="expiry">Expiry</label>
                        <input id="expiry" class="input" formControlName="expiry" placeholder="MM/YY" maxlength="5" />
                      </div>
                      <div class="field">
                        <label for="cvc">CVC</label>
                        <input id="cvc" class="input" formControlName="cvc" maxlength="4" inputmode="numeric" />
                      </div>
                    </form>

                    <div>
                      <div class="strong small">Payment outcome to simulate</div>
                      <p class="xs muted">Pick a scenario to see how the event-driven pipeline behaves.</p>
                    </div>
                    <div class="scenarios">
                      @for (s of scenarios; track s.id) {
                        <button type="button" class="scenario" [class]="'scenario ' + s.tone" [class.selected]="scenario() === s.id" (click)="scenario.set(s.id)">
                          <span class="s-icon"><app-icon [name]="s.icon" [size]="18" /></span>
                          <span class="strong">{{ s.title }}</span>
                          <span class="xs muted">{{ s.desc }}</span>
                        </button>
                      }
                    </div>

                    @if (error()) { <div class="alert alert-danger"><app-icon name="alert-circle" /> {{ error() }}</div> }

                    <div class="nav-row">
                      <button type="button" class="btn btn-ghost" (click)="goTo(1)"><app-icon name="arrow-left" [size]="16" /> Back</button>
                      <button type="button" class="btn btn-primary btn-lg" (click)="placeOrder()" [disabled]="placing()">
                        @if (placing()) { <span class="spinner"></span> Placing order… } @else { <app-icon name="lock" [size]="16" /> Pay {{ cart.subtotal() | currency }} }
                      </button>
                    </div>
                  </div>
                </section>
              }
            }
          </div>

          <aside class="card summary">
            <div class="card-header"><h3>Order summary</h3></div>
            <div class="card-pad stack">
              @for (line of cart.lines(); track line.productId) {
                <div class="row-between small"><span class="muted ellipsis">{{ line.quantity }} × {{ line.name }}</span><span>{{ line.price * line.quantity | currency }}</span></div>
              }
              <hr class="divider" />
              <div class="row-between small"><span class="muted">Subtotal</span><span>{{ cart.subtotal() | currency }}</span></div>
              <div class="row-between small"><span class="muted">Shipping</span><span class="badge badge-success">Free</span></div>
              <div class="row-between total"><span>Total</span><span>{{ cart.subtotal() | currency }}</span></div>
              <div class="alert alert-info xs">
                <app-icon name="info" [size]="16" />
                <span>Prices and stock are re-validated server-side. Your request returns <b>202 Accepted</b> instantly while payment runs asynchronously.</span>
              </div>
            </div>
          </aside>
        </div>
      }
    </div>
  `,
  styles: `
    .stepper { list-style: none; padding: 0; margin: 0 0 24px; display: grid; grid-template-columns: repeat(3, 1fr); gap: 12px; }
    .stepper button {
      width: 100%; display: flex; align-items: center; gap: 12px; padding: 12px 14px; border-radius: 14px;
      border: 1px solid var(--border); background: var(--surface); cursor: pointer; text-align: left; color: var(--text);
      transition: all 0.2s;
      &:disabled { cursor: default; opacity: 0.7; }
    }
    .num { width: 30px; height: 30px; border-radius: 50%; display: grid; place-items: center; font-weight: 700; font-size: 0.85rem; background: var(--surface-3); color: var(--text-2); flex-shrink: 0; }
    .lbl { display: flex; flex-direction: column; line-height: 1.25; font-size: 0.9rem; }
    .active button { border-color: var(--primary); box-shadow: 0 0 0 4px var(--primary-50); }
    .active .num { background: var(--primary); color: #fff; }
    .done .num { background: var(--success); color: #fff; }
    .layout { display: grid; grid-template-columns: minmax(0, 1fr) 340px; gap: 24px; align-items: start; }
    .summary { position: sticky; top: calc(var(--topbar-h) + 20px); }
    .total { font-size: 1.15rem; font-weight: 750; }
    .ellipsis { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; max-width: 210px; }
    .lines { padding: 4px 20px; }
    .line { display: flex; align-items: center; gap: 14px; padding: 14px 0; border-bottom: 1px solid var(--border); }
    .line:last-child { border-bottom: 0; }
    .thumb { width: 56px; height: 56px; border-radius: 12px; overflow: hidden; flex-shrink: 0; }
    .qty { display: flex; align-items: center; gap: 10px; font-weight: 600; }
    .amount { width: 90px; text-align: right; }
    .nav-row { display: flex; justify-content: space-between; align-items: center; gap: 12px; border-top: 1px solid var(--border); }
    .card-pad.nav-row { border-top: 1px solid var(--border); }
    form .nav-row { border-top: 0; }
    .mt { margin-top: 16px; }
    .credit-card {
      border-radius: 18px; padding: 20px 22px; color: #fff; display: flex; flex-direction: column; gap: 18px;
      background: radial-gradient(circle at 85% 15%, rgba(255,255,255,0.25), transparent 40%), linear-gradient(135deg, #312e81, #7c3aed);
      box-shadow: 0 16px 32px -12px rgba(79, 70, 229, 0.55); max-width: 380px;
    }
    .cc-number { font-size: 1.25rem; letter-spacing: 0.12em; }
    .scenarios { display: grid; grid-template-columns: repeat(3, 1fr); gap: 10px; }
    .scenario {
      display: flex; flex-direction: column; align-items: flex-start; gap: 6px; text-align: left; padding: 14px; cursor: pointer;
      border-radius: 14px; border: 1.5px solid var(--border); background: var(--surface); color: var(--text); transition: all 0.15s;
      &:hover { border-color: var(--border-strong); }
    }
    .s-icon { width: 32px; height: 32px; border-radius: 10px; display: grid; place-items: center; }
    .success .s-icon { background: var(--success-50); color: var(--success); }
    .warning .s-icon { background: var(--warning-50); color: var(--warning); }
    .danger .s-icon { background: var(--danger-50); color: var(--danger); }
    .scenario.selected.success { border-color: var(--success); box-shadow: 0 0 0 4px var(--success-50); }
    .scenario.selected.warning { border-color: var(--warning); box-shadow: 0 0 0 4px var(--warning-50); }
    .scenario.selected.danger { border-color: var(--danger); box-shadow: 0 0 0 4px var(--danger-50); }
    .alert { align-items: center; }
    @media (max-width: 960px) {
      .layout { grid-template-columns: 1fr; }
      .summary { position: static; }
      .scenarios { grid-template-columns: 1fr; }
    }
  `
})
export class CheckoutPage {
  private readonly api = inject(OrdersApi);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly fb = inject(NonNullableFormBuilder);
  protected readonly cart = inject(CartStore);

  protected readonly stepLabels = [
    { label: 'Cart', hint: 'Review items' },
    { label: 'Shipping', hint: 'Delivery address' },
    { label: 'Payment', hint: 'Simulate outcome' }
  ];
  protected readonly scenarios = SCENARIOS;
  protected readonly countries = ['India', 'United States', 'United Kingdom', 'Canada', 'Germany', 'Australia', 'Singapore'];

  protected readonly step = signal<Step>(0);
  protected readonly maxReached = signal<Step>(0);
  protected readonly scenario = signal<PaymentScenario>('approve');
  protected readonly placing = signal(false);
  protected readonly error = signal<string | null>(null);

  /** One idempotency key per checkout attempt: double-clicks and retries never create duplicate orders. */
  private idempotencyKey = crypto.randomUUID();

  protected readonly shipping = this.fb.group({
    fullName: [this.auth.user()?.fullName ?? '', [Validators.required, Validators.maxLength(120)]],
    line1: ['221B MG Road', [Validators.required, Validators.maxLength(200)]],
    line2: [''],
    city: ['Bengaluru', Validators.required],
    state: ['Karnataka', Validators.required],
    postalCode: ['560001', Validators.required],
    country: ['India', Validators.required]
  });

  protected readonly payment = this.fb.group({
    cardNumber: ['4242 4242 4242 4242', [Validators.required, Validators.pattern(/^[0-9 ]{12,19}$/)]],
    cardName: [this.auth.user()?.fullName ?? '', Validators.required],
    expiry: ['12/29', [Validators.required, Validators.pattern(/^(0[1-9]|1[0-2])\/\d{2}$/)]],
    cvc: ['123', [Validators.required, Validators.pattern(/^\d{3,4}$/)]]
  });

  private readonly cardDigits = signal(this.payment.controls.cardNumber.value.replace(/\D/g, ''));
  protected readonly maskedCard = computed(() => {
    const digits = this.cardDigits().padEnd(16, '•');
    return `•••• •••• •••• ${digits.slice(-4)}`;
  });

  goTo(step: Step) {
    if (step > this.maxReached()) return;
    this.step.set(step);
  }

  advance(step: Step) {
    if (step > this.maxReached()) this.maxReached.set(step);
    this.step.set(step);
  }

  submitShipping() {
    if (this.shipping.invalid) {
      this.shipping.markAllAsTouched();
      return;
    }
    this.advance(2);
  }

  formatCard() {
    const digits = this.payment.controls.cardNumber.value.replace(/\D/g, '').slice(0, 16);
    this.cardDigits.set(digits);
    this.payment.controls.cardNumber.setValue(digits.replace(/(.{4})/g, '$1 ').trim(), { emitEvent: false });
  }

  placeOrder() {
    if (this.step() === 0) return this.advance(1);
    if (this.payment.invalid) {
      this.payment.markAllAsTouched();
      this.error.set('Please complete the card details.');
      return;
    }
    if (this.shipping.invalid) {
      this.goTo(1);
      return;
    }

    this.placing.set(true);
    this.error.set(null);
    const s = this.shipping.getRawValue();
    const digits = this.payment.controls.cardNumber.value.replace(/\D/g, '');

    this.api
      .place(
        {
          items: this.cart.lines().map(l => ({ productId: l.productId, quantity: l.quantity })),
          shippingAddress: { ...s, line2: s.line2 || null },
          payment: { scenario: this.scenario(), cardLast4: digits.slice(-4) }
        },
        this.idempotencyKey
      )
      .subscribe({
        next: order => {
          this.cart.clear();
          this.idempotencyKey = crypto.randomUUID();
          this.toast.success(`Order ${order.orderNumber} placed`, 'Payment is being processed asynchronously — watch it update live.');
          this.router.navigate(['/orders'], { queryParams: { highlight: order.id } });
        },
        error: err => {
          this.error.set(problemMessage(err));
          this.placing.set(false);
        }
      });
  }
}
