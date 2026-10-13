export type Role = 'Admin' | 'Customer';

export interface User {
  id: string;
  email: string;
  fullName: string;
  role: Role;
}

export interface AuthResponse {
  accessToken: string;
  accessTokenExpiresAtUtc: string;
  refreshToken: string;
  user: User;
}

export interface Product {
  id: string;
  name: string;
  description: string;
  category: string;
  price: number;
  stock: number;
  imageUrl?: string | null;
  rating: number;
  updatedAtUtc: string;
}

export interface UpsertProduct {
  name: string;
  description: string;
  category: string;
  price: number;
  stock: number;
  imageUrl?: string | null;
  rating?: number | null;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface Category {
  name: string;
  count: number;
}

export type OrderStatus = 'Pending' | 'PaymentProcessing' | 'Completed' | 'Failed';
export type PaymentScenario = 'approve' | 'decline' | 'chaos';

export interface ShippingAddress {
  fullName: string;
  line1: string;
  line2?: string | null;
  city: string;
  state: string;
  postalCode: string;
  country: string;
}

export interface OrderItem {
  productId: string;
  productName: string;
  unitPrice: number;
  quantity: number;
  lineTotal: number;
}

export interface OrderStatusChange {
  status: OrderStatus;
  note: string;
  occurredAtUtc: string;
}

export interface Order {
  id: string;
  orderNumber: string;
  userId: string;
  customerName: string;
  customerEmail: string;
  status: OrderStatus;
  subtotal: number;
  discount: number;
  couponCode?: string | null;
  total: number;
  paymentScenario: PaymentScenario;
  cardLast4: string;
  transactionReference?: string | null;
  failureReason?: string | null;
  shippingAddress: ShippingAddress;
  items: OrderItem[];
  history: OrderStatusChange[];
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface PlaceOrderRequest {
  items: { productId: string; quantity: number }[];
  shippingAddress: ShippingAddress;
  payment: { scenario: PaymentScenario; cardLast4: string };
  couponCode?: string | null;
}

/** A weekly deals coupon: percentOff applies to productIds only, until expiresAtUtc. */
export interface Coupon {
  code: string;
  percentOff: number;
  productIds: string[];
  expiresAtUtc: string;
}

export interface OrderStats {
  totalOrders: number;
  pending: number;
  paymentProcessing: number;
  completed: number;
  failed: number;
  revenue: number;
  averageOrderValue: number;
  ordersLast24h: number;
}

export type Severity = 'Info' | 'Success' | 'Warning' | 'Error';

export interface EventLog {
  id: string;
  eventId: string;
  eventType: string;
  source: string;
  orderNumber?: string | null;
  summary: string;
  severity: Severity;
  payload: string;
  occurredAtUtc: string;
}

export interface EventStats {
  last24h: { eventType: string; count: number }[];
  totalEvents24h: number;
  deadLetters: number;
  emailsSent24h: number;
}

export interface DeadLetter {
  id: string;
  messageType: string;
  orderNumber?: string | null;
  exceptionType: string;
  exceptionMessage: string;
  host?: string | null;
  faultedAtUtc: string;
}

export interface NotificationMessage {
  id: string;
  userId?: string | null;
  recipient: string;
  subject: string;
  body: string;
  channel: string;
  sentAtUtc: string;
}

export interface PaymentRecord {
  id: string;
  orderId: string;
  orderNumber: string;
  customerEmail: string;
  amount: number;
  cardLast4: string;
  status: 'Succeeded' | 'Declined';
  transactionReference?: string | null;
  failureReason?: string | null;
  processedAtUtc: string;
}

export interface ServiceHealth {
  name: string;
  status: 'Healthy' | 'Degraded' | 'Unhealthy' | 'Unreachable' | string;
  latencyMs?: number | null;
  details?: {
    status: string;
    durationMs: number;
    checks: { name: string; status: string; durationMs: number; description?: string | null }[];
  } | null;
}

export interface SystemHealth {
  status: 'Healthy' | 'Degraded' | 'Unhealthy';
  checkedAtUtc: string;
  services: ServiceHealth[];
}

export interface AssistantTurn {
  role: 'user' | 'assistant';
  content: string;
}

export interface AssistantReply {
  reply: string;
  products: Product[];
}

export interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
  errors?: Record<string, string[]>;
}
