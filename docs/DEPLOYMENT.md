# Deploying OrderFlow for free (no credit card)

Everything runs on free tiers that only need an email, GitHub or Google sign-up:

| Piece | Provider (free plan) | What it hosts |
| --- | --- | --- |
| 6 backend services + frontend | **Render**: free web services, free static site | Docker containers and the Angular SPA on a CDN |
| PostgreSQL | **Neon**: Free | One database, one schema per service |
| RabbitMQ | **CloudAMQP**: Little Lemur (free) | Message broker |
| Redis | **Upstash**: Free | Catalog cache |
| CI | **GitHub Actions** | Build, unit tests, end-to-end smoke test |

Pick the **Singapore / ap-southeast-1** region everywhere it's offered. It's closest to India, and keeping all the pieces in one region keeps latency low.

---

## 1. Create the infrastructure (about 10 minutes)

### 1a. Neon (PostgreSQL)
1. Sign up at <https://neon.tech> with GitHub or Google.
2. Create a project. Pick region **AWS Asia Pacific (Singapore)** and the latest Postgres version.
3. On the project dashboard, open **Connect**. Turn **connection pooling off** to get the direct connection, then copy the connection string. It looks like:
   `postgresql://neondb_owner:xxxx@ep-xxxx.ap-southeast-1.aws.neon.tech/neondb?sslmode=require&channel_binding=require`
4. Save it as **`DATABASE_URL`**.

> Use the direct (non-pooled) URL. EF Core migrations take a session-level advisory lock, which PgBouncer's transaction pooling doesn't support.

### 1b. CloudAMQP (RabbitMQ)
1. Sign up at <https://www.cloudamqp.com>.
2. **Create New Instance**, choose the free **Little Lemur** plan and the closest region offered.
3. Open the instance and copy the **AMQP URL** (`amqps://user:password@xxx.rmq.cloudamqp.com/vhost`).
4. Save it as **`RABBITMQ_URL`**.

### 1c. Upstash (Redis)
1. Sign up at <https://upstash.com>.
2. **Create Database** (Redis), region **ap-southeast-1 (Singapore)**, Free plan.
3. On the database page, copy the Redis connection URL that starts with `rediss://`, **not** the REST URL. It looks like `rediss://default:xxxx@xxxx.upstash.io:6379`.
4. Save it as **`REDIS_URL`**.

### 1d. JWT signing secret
Generate a random secret (any string of 32+ characters) and save it as **`JWT_SECRET`**:

```powershell
# PowerShell
[Convert]::ToBase64String((1..48 | ForEach-Object { Get-Random -Maximum 256 }))
```

### 1e. AI assistant key (optional, free)
The shopping assistant runs on any OpenAI-compatible provider. Google Gemini and Groq both have free tiers with no card; Groq's daily limits are much higher. For Groq set `ASSISTANT_API_KEY`, `ASSISTANT_ENDPOINT=https://api.groq.com/openai/v1/` and `ASSISTANT_MODEL=openai/gpt-oss-120b`. For Gemini:
1. Open <https://aistudio.google.com/apikey> and sign in with a Google account.
2. Click **Create API key** and copy it.
3. Save it as **`GEMINI_API_KEY`**.

Without this key the app works normally and the **Ask AI** button stays hidden. Optional settings on the gateway: `ASSISTANT_MODEL` (default `gemini-flash-latest`), `ASSISTANT_DAILY_LIMIT` (default 300 requests a day), and `ASSISTANT_ENDPOINT` to use any other OpenAI-compatible provider.

---

## 2. Push the code to GitHub

Create an **empty** repository on GitHub (for example `orderflow`), then from the project folder:

```bash
git remote add origin https://github.com/<your-username>/orderflow.git
git push -u origin main
```

GitHub Actions starts automatically and builds, tests and runs the end-to-end smoke test.

---

## 3. Deploy on Render

1. Sign up at <https://render.com> **with your GitHub account**. No card is needed for free services.
2. **Create the secrets group.** In the dashboard open **Environment Groups → New Environment Group**, name it exactly **`orderflow-secrets`** and add four variables:

   | Key | Value |
   | --- | --- |
   | `DATABASE_URL` | from step 1a |
   | `RABBITMQ_URL` | from step 1b |
   | `REDIS_URL` | from step 1c |
   | `JWT_SECRET` | from step 1d |
   | `GEMINI_API_KEY` | from step 1e (optional) |

3. **Apply the Blueprint.** Go to **New → Blueprint**, connect your `orderflow` repository and click **Apply**. Render reads [`render.yaml`](../render.yaml) and creates:

   | Service | URL |
   | --- | --- |
   | Frontend | `https://meetul-orderflow.onrender.com` |
   | API gateway (also hosts the Identity module) | `https://meetul-orderflow-api.onrender.com` |
   | Catalog, Ordering, Payment, Notification | `https://meetul-orderflow-<name>.onrender.com` |

4. The first Docker builds take about 5–10 minutes. Each service applies its own database migrations and seeds demo data on first start.

5. **Check the URLs.** If Render shows a different URL for any service (for example with a random suffix because the name was taken), update the matching value:
   - `Services__*` on **meetul-orderflow-api** (and `Services__Catalog` on **meetul-orderflow-ordering**)
   - `API_URL` on the **meetul-orderflow** static site, then trigger a manual deploy so the SPA is rebuilt

---

## 4. Verify

- Open the frontend URL. The first load shows a "waking up services" banner while the free instances start (up to about a minute).
- Sign in with a demo account and place an order with each payment scenario.
- Or run the automated end-to-end check from your machine:

```bash
node scripts/smoke-test.mjs https://meetul-orderflow-api.onrender.com
```

---

## Free-tier behaviour and limits

- **Sleeping services.** Render spins a free service down after 15 minutes without inbound HTTP traffic and wakes it on the next request (about a minute). The gateway's `/api/system/warmup`, which the frontend calls on load and every 10 minutes while the tab is visible, wakes every service in parallel. That matters for the payment and notification workers: they consume from RabbitMQ and get no HTTP traffic of their own.
- **Fast sign-in.** Identity runs inside the gateway, so signing in only needs one service awake. If the gateway is still starting, the login and register buttons show "Waking up the live demo…" and retry automatically for up to two minutes instead of failing.
- **Keeping the demo awake during the day.** A free [cron-job.org](https://cron-job.org) account can send a `HEAD` request to `https://meetul-orderflow-api.onrender.com/health` and `https://meetul-orderflow-catalog.onrender.com/health` with the schedule `*/10 10-18 * * *` (timezone Asia/Kolkata). That keeps sign-in and the catalog instant from 10:00 to 19:00 every day for about 560 of the 750 free hours. Use `HEAD`, not `GET`: while a service is waking, Render answers with a large HTML page that cron-job.org rejects.
- **Render:** 750 free instance-hours per month across all services. Sleeping services don't consume hours.
- **Neon:** compute scales to zero when idle. The first query after a pause takes a second or two, and the services retry during startup.
- **CloudAMQP Little Lemur:** a limited number of connections and messages per month, plenty for a portfolio demo. Prefetch is kept low.
- **Upstash:** a monthly command quota. Each catalog read costs about two Redis commands.

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| A service fails with `DATABASE_URL … must be configured` | The `orderflow-secrets` group is missing or misspelled. Check its name and keys, then redeploy. |
| `/api/system/health` shows a service as `Unreachable` | The service is still waking up, or its URL in `Services__*` doesn't match the real Render URL. |
| Browser console shows CORS errors | The frontend isn't on `*.onrender.com`. Add its origin to `CORS_ORIGINS` on the gateway. |
| Orders stay in **Pending** | The payment worker is asleep or can't reach RabbitMQ. Open the System monitor or wait for the warm-up, and check `RABBITMQ_URL`. |
| Catalog health shows Redis unhealthy | Use the `rediss://` TCP URL from Upstash, not the REST URL. |
