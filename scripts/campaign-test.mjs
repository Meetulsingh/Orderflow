#!/usr/bin/env node
// End-to-end test of the weekly deals campaign through the gateway.
// Usage: node scripts/campaign-test.mjs [gatewayUrl]        (default http://localhost:5000)
// WARNING: against a deployment with BREVO_API_KEY set, this sends a real email to every opted-in customer.

const API = (process.argv[2] || process.env.API_URL || 'http://localhost:5000').replace(/\/+$/, '');
const sleep = ms => new Promise(r => setTimeout(r, ms));
let passed = 0;
let failed = 0;

function check(name, condition, detail = '') {
  if (condition) { passed++; console.log(`  ✔ ${name}`); }
  else { failed++; console.log(`  ✘ ${name} ${detail}`); }
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
  return { status: res.status, json };
}

async function waitFor(fn, predicate, timeoutMs = 120000) {
  const deadline = Date.now() + timeoutMs;
  let last;
  while (Date.now() < deadline) {
    last = await fn();
    if (predicate(last)) return last;
    await sleep(2000);
  }
  return last;
}

const login = async (email, password) => (await call('POST', '/api/auth/login', { body: { email, password } })).json?.accessToken;
const address = { fullName: 'Deal Tester', line1: '1 Test St', city: 'Pune', state: 'MH', postalCode: '411001', country: 'India' };
const order = (items, couponCode) => ({ items, shippingAddress: address, payment: { scenario: 'approve', cardLast4: '4242' }, couponCode });
const round2 = n => Math.round(n * 100) / 100;

async function main() {
  console.log(`OrderFlow weekly deals test against ${API}\n`);
  const admin = await login('admin@orderflow.dev', 'Admin@12345');
  const customer = await login('demo@orderflow.dev', 'Demo@12345');
  check('admin and customer sign in', admin && customer);

  console.log('\nSecurity');
  const anon = await call('POST', '/api/system/campaigns/weekly-deals');
  check('anonymous trigger rejected (401)', anon.status === 401, `status ${anon.status}`);
  const asCustomer = await call('POST', '/api/system/campaigns/weekly-deals', { token: customer });
  check('customer trigger rejected (401)', asCustomer.status === 401, `status ${asCustomer.status}`);
  const recipients = await call('GET', '/api/internal/marketing/recipients', { token: admin });
  check('recipient list needs the internal key (401)', recipients.status === 401, `status ${recipients.status}`);
  const badUnsub = await call('GET', '/api/auth/unsubscribe?u=00000000-0000-0000-0000-000000000000&t=bad');
  check('forged unsubscribe link rejected (400)', badUnsub.status === 400, `status ${badUnsub.status}`);

  console.log('\nCampaign');
  const before = (await call('GET', '/api/coupons/active')).json?.code ?? null;
  const started = await call('POST', '/api/system/campaigns/weekly-deals', { token: admin });
  check('admin starts a campaign (202)', started.status === 202, `status ${started.status}`);

  const active = await waitFor(() => call('GET', '/api/coupons/active'), r => r.json?.code && r.json.code !== before);
  const deal = active.json;
  check('a new coupon becomes active', deal?.code && deal.code !== before, JSON.stringify(active.json));
  if (!deal?.code) return finish();
  check('coupon covers 3 products', deal.productIds?.length === 3, `${deal.productIds?.length}`);
  check('15-30% off', [15, 20, 25, 30].includes(deal.percentOff), `${deal.percentOff}`);
  const hours = (new Date(deal.expiresAtUtc) - Date.now()) / 36e5;
  check('expires in about 72 hours', hours > 70 && hours <= 72.1, `${hours.toFixed(1)}h`);

  const checked = await call('GET', `/api/coupons/${deal.code.toLowerCase()}`, { token: customer });
  check('code check is case-insensitive', checked.status === 200 && checked.json?.code === deal.code, `status ${checked.status}`);
  const unknown = await call('GET', '/api/coupons/DEALS-ZZZZZ', { token: customer });
  check('unknown code → 404', unknown.status === 404, `status ${unknown.status}`);

  console.log('\nCheckout with the coupon');
  const all = (await call('GET', '/api/products?pageSize=48')).json.items;
  const onSale = all.find(p => deal.productIds.includes(p.id) && p.stock > 1);
  const fullPrice = all.find(p => !deal.productIds.includes(p.id) && p.stock > 1);
  const placed = await call('POST', '/api/orders', {
    token: customer,
    body: order([{ productId: onSale.id, quantity: 1 }, { productId: fullPrice.id, quantity: 1 }], deal.code.toLowerCase()),
    headers: { 'Idempotency-Key': crypto.randomUUID() }
  });
  const expectedDiscount = round2(onSale.price - Math.round(onSale.price * (100 - deal.percentOff)) / 100);
  check('order accepted with coupon', placed.status === 202, `status ${placed.status} ${JSON.stringify(placed.json)}`);
  check('discount only on the deal product', placed.json?.discount === expectedDiscount, `got ${placed.json?.discount}, expected ${expectedDiscount}`);
  check('total = subtotal - discount', round2(placed.json?.subtotal - placed.json?.discount) === placed.json?.total, JSON.stringify(placed.json && { s: placed.json.subtotal, d: placed.json.discount, t: placed.json.total }));
  check('order remembers the code', placed.json?.couponCode === deal.code);

  const wrongItems = await call('POST', '/api/orders', {
    token: customer, body: order([{ productId: fullPrice.id, quantity: 1 }], deal.code),
    headers: { 'Idempotency-Key': crypto.randomUUID() }
  });
  check('coupon on a cart with no deal products → 422', wrongItems.status === 422, `status ${wrongItems.status}`);

  const paid = await waitFor(() => call('GET', `/api/orders/${placed.json.id}`, { token: customer }), r => r.json?.status === 'Completed', 60000);
  check('discounted order is paid', paid.json?.status === 'Completed', `status ${paid.json?.status}`);
  const payments = await waitFor(() => call('GET', '/api/admin/payments', { token: admin }),
    r => r.json?.some(p => p.orderNumber === placed.json.orderNumber), 30000);
  const payment = payments.json?.find(p => p.orderNumber === placed.json.orderNumber);
  check('payment charged the discounted total', payment?.amount === placed.json.total, `charged ${payment?.amount}`);

  console.log('\nEmail');
  const logged = await waitFor(() => call('GET', '/api/admin/events?take=100', { token: admin }),
    r => r.json?.some(e => e.eventType === 'WeeklyDealsLaunched' && e.summary.includes(deal.code)), 120000);
  const entry = logged.json?.find(e => e.eventType === 'WeeklyDealsLaunched' && e.summary.includes(deal.code));
  check('Notification service processed the campaign', !!entry, '');
  if (entry) console.log(`    ${entry.summary}`);

  finish();
}

function finish() {
  console.log(`\n${passed} passed, ${failed} failed`);
  process.exit(failed === 0 ? 0 : 1);
}

main().catch(err => { console.error(err); process.exit(1); });
