import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, catchError, finalize, map, of, shareReplay, throwError } from 'rxjs';
import { API_URL } from '../config';
import { AuthResponse, User } from '../models';
import { SILENT_ERRORS, SKIP_AUTH } from '../http-context';
import { retryWhileWaking } from '../warmup.service';

interface Session {
  accessToken: string;
  expiresAt: number;
  refreshToken: string;
  user: User;
}

const STORAGE_KEY = 'orderflow.session';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  private readonly session = signal<Session | null>(this.restore());
  private refreshInFlight$: Observable<string> | null = null;

  /** True while a sign-in is being retried because the backend is still waking up. */
  readonly waking = signal(false);

  readonly user = computed(() => this.session()?.user ?? null);
  readonly isAuthenticated = computed(() => this.session() !== null);
  readonly isAdmin = computed(() => this.session()?.user.role === 'Admin');

  get accessToken(): string | null {
    return this.session()?.accessToken ?? null;
  }

  get isAccessTokenFresh(): boolean {
    const s = this.session();
    return !!s && s.expiresAt - Date.now() > 30_000;
  }

  login(email: string, password: string): Observable<User> {
    return this.http
      .post<AuthResponse>(`${API_URL}/api/auth/login`, { email, password }, { context: skipAuth() })
      .pipe(
        retryWhileWaking(() => this.waking.set(true)),
        finalize(() => this.waking.set(false)),
        map(r => this.store(r))
      );
  }

  register(fullName: string, email: string, password: string): Observable<User> {
    return this.http
      .post<AuthResponse>(`${API_URL}/api/auth/register`, { fullName, email, password }, { context: skipAuth() })
      .pipe(
        retryWhileWaking(() => this.waking.set(true)),
        finalize(() => this.waking.set(false)),
        map(r => this.store(r))
      );
  }

  /** Rotates the refresh token. Concurrent callers share a single in-flight request. */
  refresh(): Observable<string> {
    const refreshToken = this.session()?.refreshToken;
    if (!refreshToken) return throwError(() => new Error('No refresh token'));

    this.refreshInFlight$ ??= this.http
      .post<AuthResponse>(`${API_URL}/api/auth/refresh`, { refreshToken }, { context: skipAuth() })
      .pipe(
        map(r => {
          this.store(r);
          return r.accessToken;
        }),
        catchError(err => {
          this.clear();
          return throwError(() => err);
        }),
        finalize(() => (this.refreshInFlight$ = null)),
        shareReplay(1)
      );
    return this.refreshInFlight$;
  }

  /** Returns a valid access token, refreshing first if it is about to expire (used by SignalR). */
  async getValidAccessToken(): Promise<string> {
    if (this.isAccessTokenFresh) return this.accessToken!;
    return new Promise<string>((resolve, reject) => this.refresh().subscribe({ next: resolve, error: reject }));
  }

  logout(redirect = true): void {
    const refreshToken = this.session()?.refreshToken;
    if (refreshToken) {
      this.http
        .post(`${API_URL}/api/auth/logout`, { refreshToken }, { context: skipAuth() })
        .pipe(catchError(() => of(null)))
        .subscribe();
    }
    this.clear();
    if (redirect) this.router.navigateByUrl('/login');
  }

  expireSession(): void {
    this.clear();
    this.router.navigate(['/login'], { queryParams: { expired: 1 } });
  }

  private store(r: AuthResponse): User {
    const session: Session = {
      accessToken: r.accessToken,
      expiresAt: new Date(r.accessTokenExpiresAtUtc).getTime(),
      refreshToken: r.refreshToken,
      user: r.user
    };
    this.session.set(session);
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(session));
    } catch { /* storage unavailable */ }
    return r.user;
  }

  private clear(): void {
    this.session.set(null);
    try {
      localStorage.removeItem(STORAGE_KEY);
    } catch { /* storage unavailable */ }
  }

  private restore(): Session | null {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      return raw ? (JSON.parse(raw) as Session) : null;
    } catch {
      return null;
    }
  }
}

function skipAuth(): HttpContext {
  // Auth calls render their own inline errors, so they also opt out of global toasts.
  return new HttpContext().set(SKIP_AUTH, true).set(SILENT_ERRORS, true);
}

