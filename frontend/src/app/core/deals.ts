import { Coupon } from './models';

/** The coupon's price for one unit, rounded to cents exactly like the Ordering service does. */
export function dealPrice(price: number, percentOff: number): number {
  return Math.round(price * (100 - percentOff)) / 100;
}

export function inDeal(coupon: Coupon | null | undefined, productId: string): boolean {
  return !!coupon && coupon.productIds.includes(productId);
}

/** "Thu 16 Oct, 10:00 AM" in the viewer's own time zone. */
export function formatDealEnd(iso: string): string {
  return new Date(iso).toLocaleString(undefined, { weekday: 'short', day: 'numeric', month: 'short', hour: 'numeric', minute: '2-digit' });
}
