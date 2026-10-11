import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { MonoTypeOperatorFunction, firstValueFrom, retry, throwError, timer } from 'rxjs';
import { SystemApi } from './api.service';
import { ServiceHealth } from './models';

export type WarmupPhase = 'idle' | 'waking' | 'ready' | 'degraded';

/** What the browser sees while the host starts a sleeping service: no response at all, or a proxy error. */
const WAKING_STATUSES = new Set([0, 502, 503, 504]);

/**
 * Retries a request for up to two minutes while the backend is cold-starting, so the first click after
 * an idle period still succeeds instead of showing an error. Real errors (400, 401, 409, …) fail at once.
 */
export function retryWhileWaking<T>(onWaiting?: () => void): MonoTypeOperatorFunction<T> {
  return retry({
    count: 24,
    delay: (error: unknown, attempt: number) => {
      if (!(error instanceof HttpErrorResponse) || !WAKING_STATUSES.has(error.status)) return throwError(() => error);
      if (attempt === 1) onWaiting?.();
      return timer(5000);
    }
  });
}

/**
 * Free-tier hosting puts idle services to sleep. On load the app asks the gateway to wake every
 * microservice in parallel and shows progress until they all report healthy.
 */
@Injectable({ providedIn: 'root' })
export class WarmupService {
  private readonly api = inject(SystemApi);
  private started = false;

  readonly phase = signal<WarmupPhase>('idle');
  readonly services = signal<ServiceHealth[]>([]);
  readonly readyCount = computed(() => this.services().filter(s => s.status === 'Healthy').length);
  readonly total = computed(() => this.services().length || 5);
  readonly dismissed = signal(false);

  start(): void {
    if (this.started) return;
    this.started = true;
    void this.run();

    // Keep services warm while the tab is open and visible.
    setInterval(() => {
      if (document.visibilityState === 'visible') this.api.warmup().subscribe({ error: () => undefined });
    }, 10 * 60 * 1000);
  }

  private async run(): Promise<void> {
    const deadline = Date.now() + 3 * 60 * 1000;
    let firstCheck = true;

    while (Date.now() < deadline) {
      try {
        await firstValueFrom(this.api.warmup());
        const health = await firstValueFrom(this.api.health());
        this.services.set(health.services);
        if (health.services.every(s => s.status === 'Healthy')) {
          this.phase.set('ready');
          return;
        }
      } catch {
        // Gateway itself is still starting.
      }
      if (firstCheck) {
        this.phase.set('waking');
        firstCheck = false;
      }
      await new Promise(r => setTimeout(r, 4000));
    }
    this.phase.set('degraded');
  }
}
