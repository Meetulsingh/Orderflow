#!/usr/bin/env node
// End-to-end smoke test through the API gateway.
// Usage: node scripts/smoke-test.mjs [gatewayUrl]     (default http://localhost:5000)
// Exercises auth, caching, CQRS order placement, the async payment pipeline, DLQ handling and admin APIs.

const API = (process.argv[2] || process.env.API_URL || 'http://localhost:5000').replace(/\/+$/, '');
const sleep = ms => new Promise(r => setTimeout(r, ms));
let passed = 0;
let failed = 0;

function check(name, condition, detail = '') {
  if (condition) {
    passed++;
    console.log(`  ✔ ${name}`);
  } else {
    failed++;
    console.log(`  ✘ ${name} ${detail}`);
  }
}

async function call(method, path, { token, body, headers = {} } = {}) {
  const res = await fetch(`${API}${path}`, {
    method,
    headers: {
      ...(body ? { 'Content-Type': 'application/json' } : {}),
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...headers
    },
    body: body ? JSON.stringify(body) : undefined
  });
  const text = await res.text();
  let json = null;
  try { json = text ? JSON.parse(text) : null; } catch { /* not json */ }
  return { status: res.status, json, headers: res.headers };
}

async function waitFor(fn, predicate, timeoutMs = 45000) {
  const deadline = Date.now() + timeoutMs;
  let last;
  while (Date.now() < deadline) {
    last = await fn();
    if (predicate(last)) return last;
    await sleep(1000);
  }
  return last;
}

const address = { fullName: 'Smoke Test', line1: '1 Test St', city: 'Pune', state: 'MH', postalCode: '411001', country: 'India' };

async function main() {
  console.log(`OrderFlow smoke test against ${API}\n`);

  console.log('Waking services…');
  await call('POST', '/api/system/warmup');
  const health = await waitFor(() => call('GET', '/api/system/health'), r => r.json?.status === 'Healthy', 180000);
  check('all services healthy', health.json?.status === 'Healthy', JSON.stringify(health.json?.services?.map(s => `${s.name}:${s.status}`)));

  console.log('\nIdentity');
  const customer = await call('POST', '/api/auth/login', { body: { email: 'demo@orderflow.dev', password: 'Demo@12345' } });
  check('demo customer login', customer.status === 200 && customer.json?.accessToken, `status ${customer.status}`);
  const admin = await call('POST', '/api/auth/login', { body: { email: 'admin@orderflow.dev', password: 'Admin@12345' } });
  check('demo admin login', admin.status === 200 && admin.json?.user?.role === 'Admin', `status ${admin.status}`);
  const badLogin = await call('POST', '/api/auth/login', { body: { email: 'demo@orderflow.dev', password: 'wrong' } });
  check('wrong password → 401', badLogin.status === 401, `status ${badLogin.status}`);
  const email = `smoke+${Date.now()}@example.com`;
  const reg = await call('POST', '/api/auth/register', { body: { fullName: 'Smoke Tester', email, password: 'Sm0keTest!' } });
  check('register new user', reg.status === 200 && reg.json?.user?.email === email, `status ${reg.status}`);
  const dup = await call('POST', '/api/auth/register', { body: { fullName: 'Smoke Tester', email, password: 'Sm0keTest!' } });
  check('duplicate registration → 409', dup.status === 409, `status ${dup.status}`);
  const weak = await call('POST', '/api/auth/register', { body: { fullName: 'X', email: 'not-an-email', password: 'weak' } });
  check('invalid registration → 400 with field errors', weak.status === 400 && weak.json?.errors, `status ${weak.status}`);

  const refreshed = await call('POST', '/api/auth/refresh', { body: { refreshToken: reg.json.refreshToken } });
  check('refresh token rotation', refreshed.status === 200 && refreshed.json.refreshToken !== reg.json.refreshToken, `status ${refreshed.status}`);
  const reused = await call('POST', '/api/auth/refresh', { body: { refreshToken: reg.json.refreshToken } });
  check('reused refresh token rejected', reused.status === 401, `status ${reused.status}`);
  const me = await call('GET', '/api/auth/me', { token: refreshed.json?.accessToken });
  check('GET /me with new access token', me.status === 200 && me.json?.email === email, `status ${me.status}`);

  const token = customer.json.accessToken;
  const adminToken = admin.json.accessToken;

  console.log('\nCatalog + Redis cache');
  await call('GET', '/api/products?pageSize=48');
  const list = await call('GET', '/api/products?pageSize=48');
  check('product list', list.status === 200 && list.json?.items?.length > 0, `status ${list.status}`);
  check('second read served from cache (X-Cache: HIT)', list.headers.get('x-cache') === 'HIT', `got ${list.headers.get('x-cache')}`);
  const search = await call('GET', '/api/products?search=headphones');
  check('search filter', search.json?.items?.every(p => /headphones/i.test(p.name + p.description)), '');
  const cats = await call('GET', '/api/products/categories');
  check('categories', cats.status === 200 && cats.json?.length > 0, `status ${cats.status}`);
  const product = list.json.items.filter(p => p.stock > 10).sort((a, b) => b.stock - a.stock)[0];
  const forbidden = await call('POST', '/api/products', { token, body: { name: 'x', description: 'x', category: 'x', price: 1, stock: 1 } });
  check('customer cannot create products → 403', forbidden.status === 403, `status ${forbidden.status}`);

  console.log('\nOrdering (CQRS + outbox) → Payment → Notification');
  const unauth = await call('GET', '/api/orders');
  check('orders require auth → 401', unauth.status === 401, `status ${unauth.status}`);

  const order = scenario => ({ items: [{ productId: product.id, quantity: 1 }], shippingAddress: address, payment: { scenario, cardLast4: '4242' } });
  const key = crypto.randomUUID();
  const placed = await call('POST', '/api/orders', { token, body: order('approve'), headers: { 'Idempotency-Key': key } });
  check('place order → 202 Accepted, Pending', placed.status === 202 && placed.json?.status === 'Pending', `status ${placed.status} ${JSON.stringify(placed.json)}`);
  const again = await call('POST', '/api/orders', { token, body: order('approve'), headers: { 'Idempotency-Key': key } });
  check('same Idempotency-Key returns the same order', again.json?.id === placed.json?.id, '');

  const completed = await waitFor(() => call('GET', `/api/orders/${placed.json.id}`, { token }), r => r.json?.status === 'Completed');
  check('approved order reaches Completed asynchronously', completed.json?.status === 'Completed', `status ${completed.json?.status}`);
  check('status history recorded', completed.json?.history?.length >= 2, '');

  const after = await waitFor(
    () => call('GET', `/api/products/${product.id}`),
    r => r.json?.stock === product.stock - 1, 20000);
  check('stock deducted & cache invalidated', after.json?.stock === product.stock - 1, `expected ${product.stock - 1}, got ${after.json?.stock}`);

  const declined = await call('POST', '/api/orders', { token, body: order('decline'), headers: { 'Idempotency-Key': crypto.randomUUID() } });
  const declinedFinal = await waitFor(() => call('GET', `/api/orders/${declined.json.id}`, { token }), r => r.json?.status === 'Failed');
  check('declined payment → order Failed', declinedFinal.json?.status === 'Failed', `status ${declinedFinal.json?.status}`);

  const chaos = await call('POST', '/api/orders', { token, body: order('chaos'), headers: { 'Idempotency-Key': crypto.randomUUID() } });
  const chaosFinal = await waitFor(() => call('GET', `/api/orders/${chaos.json.id}`, { token }), r => r.json?.status === 'Failed', 60000);
  check('chaos → retries exhausted → DLQ → order Failed (compensation)', chaosFinal.json?.status === 'Failed' && /dead-letter/i.test(chaosFinal.json?.failureReason ?? ''), `status ${chaosFinal.json?.status}`);

  const outOfStock = await call('POST', '/api/orders', {
    token,
    body: { ...order('approve'), items: [{ productId: product.id, quantity: 20 }] },
    headers: { 'Idempotency-Key': crypto.randomUUID() }
  });
  check('over-ordering is rejected (422) or allowed if stock suffices', outOfStock.status === 422 || outOfStock.status === 202, `status ${outOfStock.status}`);

  const mine = await call('GET', '/api/orders', { token });
  check('GET my orders', mine.status === 200 && mine.json?.some(o => o.id === placed.json.id), `status ${mine.status}`);

  console.log('\nAdmin / observability');
  const denied = await call('GET', '/api/admin/orders/stats', { token });
  check('customer denied admin API → 403', denied.status === 403, `status ${denied.status}`);
  const stats = await call('GET', '/api/admin/orders/stats', { token: adminToken });
  check('order stats', stats.status === 200 && stats.json?.totalOrders >= 3, `status ${stats.status}`);
  const events = await waitFor(() => call('GET', '/api/admin/events?take=100', { token: adminToken }),
    r => r.json?.some(e => e.orderNumber === placed.json.orderNumber && e.eventType === 'PaymentSucceeded'), 20000);
  check('event log captured PaymentSucceeded', events.json?.some(e => e.orderNumber === placed.json.orderNumber && e.eventType === 'PaymentSucceeded'), '');
  const dlq = await waitFor(() => call('GET', '/api/admin/dead-letters', { token: adminToken }),
    r => r.json?.some(d => d.orderNumber === chaos.json.orderNumber), 20000);
  check('dead letter recorded for chaos order', dlq.json?.some(d => d.orderNumber === chaos.json.orderNumber), '');
  const mails = await call('GET', '/api/admin/notifications', { token: adminToken });
  check('notification emails generated', mails.status === 200 && mails.json?.length > 0, `status ${mails.status}`);
  const payments = await call('GET', '/api/admin/payments', { token: adminToken });
  check('payment records', payments.status === 200 && payments.json?.some(p => p.orderNumber === placed.json.orderNumber), `status ${payments.status}`);

  console.log(`\n${passed} passed, ${failed} failed`);
  process.exit(failed === 0 ? 0 : 1);
}

main().catch(err => {
  console.error(err);
  process.exit(1);
});
