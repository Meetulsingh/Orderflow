import { Injectable, effect, inject, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { AuthService } from './auth/auth.service';
import { API_URL } from './config';
import { Order } from './models';

export type HubState = 'disconnected' | 'connecting' | 'connected' | 'reconnecting';

/**
 * Maintains the SignalR connection to the Ordering service (through the gateway) while the user is
 * signed in, and exposes order status pushes as an observable stream.
 */
@Injectable({ providedIn: 'root' })
export class OrderHubService {
  private readonly auth = inject(AuthService);
  private connection: HubConnection | null = null;
  private retryTimer: ReturnType<typeof setTimeout> | null = null;

  readonly state = signal<HubState>('disconnected');
  readonly orderUpdated$ = new Subject<Order>();

  constructor() {
    effect(() => {
      if (this.auth.isAuthenticated()) void this.start();
      else void this.stop();
    });
  }

  private async start(): Promise<void> {
    if (this.connection) return;

    const connection = new HubConnectionBuilder()
      .withUrl(`${API_URL}/hubs/orders`, {
        accessTokenFactory: () => this.auth.getValidAccessToken(),
        withCredentials: false
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 20000, 30000])
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on('orderUpdated', (order: Order) => this.orderUpdated$.next(order));
    connection.onreconnecting(() => this.state.set('reconnecting'));
    connection.onreconnected(() => this.state.set('connected'));
    connection.onclose(() => {
      this.state.set('disconnected');
      this.scheduleRetry();
    });

    this.connection = connection;
    await this.connect();
  }

  private async connect(): Promise<void> {
    const connection = this.connection;
    if (!connection || connection.state !== HubConnectionState.Disconnected) return;
    this.state.set('connecting');
    try {
      await connection.start();
      this.state.set('connected');
    } catch {
      // The Ordering service may be cold-starting on free hosting; keep trying in the background.
      this.state.set('disconnected');
      this.scheduleRetry();
    }
  }

  private scheduleRetry(): void {
    if (this.retryTimer || !this.auth.isAuthenticated()) return;
    this.retryTimer = setTimeout(() => {
      this.retryTimer = null;
      void this.connect();
    }, 8000);
  }

  private async stop(): Promise<void> {
    if (this.retryTimer) {
      clearTimeout(this.retryTimer);
      this.retryTimer = null;
    }
    const connection = this.connection;
    this.connection = null;
    this.state.set('disconnected');
    if (connection) await connection.stop().catch(() => undefined);
  }
}
