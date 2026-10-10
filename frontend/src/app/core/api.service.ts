import { HttpClient, HttpContext, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { API_URL } from './config';
import { SILENT_ERRORS } from './http-context';
import {
  Category, DeadLetter, EventLog, EventStats, NotificationMessage, Order, OrderStats, PagedResult,
  PaymentRecord, PlaceOrderRequest, Product, SystemHealth, UpsertProduct
} from './models';

export interface ProductQuery {
  search?: string;
  category?: string;
  sort?: string;
  page?: number;
  pageSize?: number;
}

export interface CachedResult<T> {
  data: T;
  cache: 'HIT' | 'MISS' | null;
}

const silent = () => ({ context: new HttpContext().set(SILENT_ERRORS, true) });

@Injectable({ providedIn: 'root' })
export class CatalogApi {
  private readonly http = inject(HttpClient);
  private readonly base = `${API_URL}/api/products`;

  list(query: ProductQuery): Observable<CachedResult<PagedResult<Product>>> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(query)) {
      if (value !== undefined && value !== null && value !== '') params = params.set(key, String(value));
    }
    return this.http.get<PagedResult<Product>>(this.base, { params, observe: 'response' }).pipe(
      map(r => ({ data: r.body!, cache: (r.headers.get('X-Cache') as 'HIT' | 'MISS' | null) ?? null }))
    );
  }

  categories(): Observable<Category[]> {
    return this.http.get<Category[]>(`${this.base}/categories`);
  }

  create(product: UpsertProduct): Observable<Product> {
    return this.http.post<Product>(this.base, product, silent());
  }

  update(id: string, product: UpsertProduct): Observable<Product> {
    return this.http.put<Product>(`${this.base}/${id}`, product, silent());
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }
}

@Injectable({ providedIn: 'root' })
export class OrdersApi {
  private readonly http = inject(HttpClient);

  place(request: PlaceOrderRequest, idempotencyKey: string): Observable<Order> {
    return this.http.post<Order>(`${API_URL}/api/orders`, request, {
      headers: { 'Idempotency-Key': idempotencyKey },
      ...silent()
    });
  }

  mine(): Observable<Order[]> {
    return this.http.get<Order[]>(`${API_URL}/api/orders`);
  }

  all(take = 50): Observable<Order[]> {
    return this.http.get<Order[]>(`${API_URL}/api/admin/orders`, { params: { take } });
  }

  stats(): Observable<OrderStats> {
    return this.http.get<OrderStats>(`${API_URL}/api/admin/orders/stats`, silent());
  }
}

@Injectable({ providedIn: 'root' })
export class AdminApi {
  private readonly http = inject(HttpClient);

  events(take = 60, severity?: string): Observable<EventLog[]> {
    const params: Record<string, string | number> = { take };
    if (severity) params['severity'] = severity;
    return this.http.get<EventLog[]>(`${API_URL}/api/admin/events`, { params, ...silent() });
  }

  eventStats(): Observable<EventStats> {
    return this.http.get<EventStats>(`${API_URL}/api/admin/events/stats`, silent());
  }

  deadLetters(): Observable<DeadLetter[]> {
    return this.http.get<DeadLetter[]>(`${API_URL}/api/admin/dead-letters`, silent());
  }

  notifications(): Observable<NotificationMessage[]> {
    return this.http.get<NotificationMessage[]>(`${API_URL}/api/admin/notifications`, silent());
  }

  payments(): Observable<PaymentRecord[]> {
    return this.http.get<PaymentRecord[]>(`${API_URL}/api/admin/payments`, silent());
  }

  myNotifications(): Observable<NotificationMessage[]> {
    return this.http.get<NotificationMessage[]>(`${API_URL}/api/notifications/mine`, silent());
  }
}

@Injectable({ providedIn: 'root' })
export class SystemApi {
  private readonly http = inject(HttpClient);

  health(): Observable<SystemHealth> {
    return this.http.get<SystemHealth>(`${API_URL}/api/system/health`, silent());
  }

  warmup(): Observable<unknown> {
    return this.http.post(`${API_URL}/api/system/warmup`, null, silent());
  }
}
