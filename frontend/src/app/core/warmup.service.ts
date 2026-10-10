import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { SystemApi } from './api.service';
import { ServiceHealth } from './models';

export type WarmupPhase = 'idle' | 'waking' | 'ready' | 'degraded';

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
  readonly total = computed(() => Math.max(this.services().length, 6));
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
