import { Pipe, PipeTransform } from '@angular/core';

/** Relative time ("12s ago", "3m ago"); pure, so pair with a ticking signal to refresh. */
@Pipe({ name: 'timeAgo' })
export class TimeAgoPipe implements PipeTransform {
  transform(value: string | Date | null | undefined, _tick?: number): string {
    if (!value) return '';
    const date = typeof value === 'string' ? new Date(value) : value;
    const seconds = Math.max(0, Math.round((Date.now() - date.getTime()) / 1000));
    if (seconds < 5) return 'just now';
    if (seconds < 60) return `${seconds}s ago`;
    const minutes = Math.floor(seconds / 60);
    if (minutes < 60) return `${minutes}m ago`;
    const hours = Math.floor(minutes / 60);
    if (hours < 24) return `${hours}h ago`;
    const days = Math.floor(hours / 24);
    if (days < 7) return `${days}d ago`;
    return date.toLocaleDateString();
  }
}
