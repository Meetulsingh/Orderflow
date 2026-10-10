import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { problemMessage } from '../../core/error.interceptor';
import { ToastService } from '../../core/toast.service';
import { Icon } from '../../shared/icon';
import { AuthLayout } from './auth-layout';

const PASSWORD_RULES = [
  { label: '8+ characters', test: (v: string) => v.length >= 8 },
  { label: 'Uppercase letter', test: (v: string) => /[A-Z]/.test(v) },
  { label: 'Lowercase letter', test: (v: string) => /[a-z]/.test(v) },
  { label: 'Number', test: (v: string) => /[0-9]/.test(v) }
];

@Component({
  selector: 'app-register-page',
  imports: [ReactiveFormsModule, RouterLink, Icon, AuthLayout],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-auth-layout>
      <div>
        <h2 class="title">Create your account</h2>
        <p class="muted">It takes less than a minute. No credit card, obviously.</p>
      </div>

      <form [formGroup]="form" (ngSubmit)="submit()" class="stack" novalidate>
        <div class="field">
          <label for="name">Full name</label>
          <input id="name" class="input" formControlName="fullName" autocomplete="name" placeholder="Jane Doe" />
          @if (form.controls.fullName.touched && form.controls.fullName.invalid) { <span class="error">Please enter your name.</span> }
        </div>
        <div class="field">
          <label for="email">Email</label>
          <input id="email" class="input" type="email" formControlName="email" autocomplete="email" placeholder="you@example.com" />
          @if (form.controls.email.touched && form.controls.email.invalid) { <span class="error">Enter a valid email address.</span> }
        </div>
        <div class="field">
          <label for="password">Password</label>
          <input id="password" class="input" type="password" formControlName="password" autocomplete="new-password" placeholder="Create a strong password" />
          <div class="rules">
            @for (rule of rules(); track rule.label) {
              <span class="rule" [class.ok]="rule.ok"><app-icon [name]="rule.ok ? 'check' : 'minus'" [size]="12" [stroke]="3" />{{ rule.label }}</span>
            }
          </div>
        </div>

        @if (error()) { <div class="alert alert-danger"><app-icon name="alert-circle" /> {{ error() }}</div> }

        <button class="btn btn-primary btn-lg btn-block" type="submit" [disabled]="loading()">
          @if (loading()) { <span class="spinner"></span> Creating account… } @else { Create account <app-icon name="arrow-right" [size]="16" /> }
        </button>
      </form>

      <p class="small muted center">Already have an account? <a routerLink="/login">Sign in</a></p>
    </app-auth-layout>
  `,
  styles: `
    .title { font-size: 1.6rem; margin-bottom: 6px; }
    .center { text-align: center; }
    .rules { display: flex; flex-wrap: wrap; gap: 6px; margin-top: 4px; }
    .rule {
      display: inline-flex; align-items: center; gap: 4px; font-size: 0.74rem; font-weight: 550;
      padding: 3px 8px; border-radius: 999px; background: var(--surface-3); color: var(--text-3); transition: all 0.2s;
      &.ok { background: var(--success-50); color: var(--success); }
    }
    .alert { align-items: center; }
  `
})
export class RegisterPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);

  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly form = inject(NonNullableFormBuilder).group({
    fullName: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(120)]],
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(8), Validators.pattern(/^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).+$/)]]
  });

  private readonly password = toSignal(this.form.controls.password.valueChanges, { initialValue: '' });
  protected readonly rules = computed(() => PASSWORD_RULES.map(r => ({ label: r.label, ok: r.test(this.password()) })));

  submit() {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.loading.set(true);
    this.error.set(null);
    const { fullName, email, password } = this.form.getRawValue();
    this.auth.register(fullName, email, password).subscribe({
      next: user => {
        this.toast.success('Account created', `Welcome aboard, ${user.fullName.split(' ')[0]}! A welcome email event was published.`);
        this.router.navigateByUrl('/products');
      },
      error: err => {
        this.error.set(problemMessage(err));
        this.loading.set(false);
      }
    });
  }
}
