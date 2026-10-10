import { Injectable, signal } from '@angular/core';

export type ToastKind = 'success' | 'error' | 'info' | 'warning';

export interface Toast {
  id: number;
  kind: ToastKind;
  title: string;
  message?: string;
}

@Injectable({ providedIn: 'root' })
export class ToastService {
  private nextId = 1;
  readonly toasts = signal<Toast[]>([]);

  success(title: string, message?: string) { this.show('success', title, message); }
  error(title: string, message?: string) { this.show('error', title, message, 6000); }
  info(title: string, message?: string) { this.show('info', title, message); }
  warning(title: string, message?: string) { this.show('warning', title, message, 5000); }

  dismiss(id: number) {
    this.toasts.update(list => list.filter(t => t.id !== id));
  }

  private show(kind: ToastKind, title: string, message?: string, durationMs = 3800) {
    // Collapse identical consecutive toasts (e.g. a burst of failed requests).
    const last = this.toasts().at(-1);
    if (last && last.title === title && last.message === message) return;

    const toast: Toast = { id: this.nextId++, kind, title, message };
    this.toasts.update(list => [...list.slice(-3), toast]);
    setTimeout(() => this.dismiss(toast.id), durationMs);
  }
}
