import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { problemMessage } from '../../core/error.interceptor';
import { ToastService } from '../../core/toast.service';
import { Icon } from '../../shared/icon';
import { AuthLayout } from './auth-layout';

@Component({
  selector: 'app-login-page',
  imports: [ReactiveFormsModule, RouterLink, Icon, AuthLayout],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-auth-layout>
      <div>
        <h2 class="title">Welcome back</h2>
        <p class="muted">Sign in to place orders and track them in real time.</p>
      </div>

      @if (expired()) {
        <div class="alert alert-warning"><app-icon name="clock" /> Your session expired. Please sign in again.</div>
      }

      <div class="demo">
        <span class="xs subtle strong">ONE-CLICK DEMO ACCOUNTS</span>
        <div class="demo-grid">
          <button type="button" class="demo-card" (click)="useDemo('customer')">
            <span class="demo-icon customer"><app-icon name="user" [size]="16" /></span>
            <span><span class="strong">Customer</span><span class="xs muted block">Shop &amp; track orders</span></span>
          </button>
          <button type="button" class="demo-card" (click)="useDemo('admin')">
            <span class="demo-icon admin"><app-icon name="shield" [size]="16" /></span>
            <span><span class="strong">Admin</span><span class="xs muted block">Products &amp; system monitor</span></span>
          </button>
        </div>
      </div>

      <div class="or"><span>or sign in with email</span></div>

      <form [formGroup]="form" (ngSubmit)="submit()" class="stack" novalidate>
        <div class="field">
          <label for="email">Email</label>
          <input id="email" class="input" type="email" formControlName="email" autocomplete="email" placeholder="you@example.com" />
          @if (form.controls.email.touched && form.controls.email.invalid) { <span class="error">Enter a valid email address.</span> }
        </div>
        <div class="field">
          <label for="password">Password</label>
          <div class="pw">
            <input id="password" class="input" [type]="showPassword() ? 'text' : 'password'" formControlName="password" autocomplete="current-password" placeholder="••••••••" />
            <button type="button" class="btn btn-ghost btn-sm btn-icon eye" (click)="showPassword.set(!showPassword())" [attr.aria-label]="showPassword() ? 'Hide password' : 'Show password'">
              <app-icon [name]="showPassword() ? 'eye-off' : 'eye'" [size]="16" />
            </button>
          </div>
          @if (form.controls.password.touched && form.controls.password.invalid) { <span class="error">Password is required.</span> }
        </div>

        @if (error()) { <div class="alert alert-danger"><app-icon name="alert-circle" /> {{ error() }}</div> }

        <button class="btn btn-primary btn-lg btn-block" type="submit" [disabled]="loading()">
          @if (loading()) { <span class="spinner"></span> Signing in… } @else { Sign in <app-icon name="arrow-right" [size]="16" /> }
        </button>
      </form>

      <p class="small muted center">New to OrderFlow? <a routerLink="/register">Create an account</a></p>
    </app-auth-layout>
  `,
  styles: `
    .title { font-size: 1.6rem; margin-bottom: 6px; }
    .demo { display: flex; flex-direction: column; gap: 10px; }
    .demo-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; }
    .demo-card {
      display: flex; align-items: center; gap: 10px; text-align: left; padding: 12px; cursor: pointer;
      border: 1px solid var(--border); border-radius: 12px; background: var(--surface); color: var(--text); font-size: 0.88rem;
      transition: all 0.15s;
      &:hover { border-color: var(--primary); box-shadow: 0 0 0 4px var(--primary-50); }
    }
    .block { display: block; }
    .demo-icon { width: 32px; height: 32px; border-radius: 10px; display: grid; place-items: center; flex-shrink: 0; }
    .demo-icon.customer { background: var(--info-50); color: var(--info); }
    .demo-icon.admin { background: var(--primary-50); color: var(--primary); }
    .or { display: flex; align-items: center; gap: 12px; color: var(--text-3); font-size: 0.78rem; }
    .or::before, .or::after { content: ''; flex: 1; height: 1px; background: var(--border); }
    .pw { position: relative; }
    .pw .input { padding-right: 44px; }
    .eye { position: absolute; right: 5px; top: 5px; }
    .center { text-align: center; }
    .alert { align-items: center; }
  `
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);

  readonly returnUrl = input<string>();
  readonly expired = input<string>();

  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly showPassword = signal(false);

  protected readonly form = inject(NonNullableFormBuilder).group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required]
  });

  useDemo(kind: 'customer' | 'admin') {
    this.form.setValue(
      kind === 'admin'
        ? { email: 'admin@orderflow.dev', password: 'Admin@12345' }
        : { email: 'demo@orderflow.dev', password: 'Demo@12345' }
    );
    this.submit();
  }

  submit() {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.loading.set(true);
    this.error.set(null);
    const { email, password } = this.form.getRawValue();
    this.auth.login(email, password).subscribe({
      next: user => {
        this.toast.success(`Welcome, ${user.fullName.split(' ')[0]}!`);
        this.router.navigateByUrl(this.returnUrl() || (user.role === 'Admin' ? '/admin/logs' : '/products'));
      },
      error: err => {
        this.error.set(problemMessage(err));
        this.loading.set(false);
      }
    });
  }
}
