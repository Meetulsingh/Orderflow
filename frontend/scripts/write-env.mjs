// Writes public/env.js so the API URL can be configured per environment without rebuilding code.
// Usage: API_URL=https://my-gateway.onrender.com node scripts/write-env.mjs
import { writeFileSync } from 'node:fs';

const apiUrl = (process.env.API_URL || 'http://localhost:5000').replace(/\/+$/, '');
const contents = `window.__env = ${JSON.stringify({ apiUrl }, null, 2)};\n`;
writeFileSync(new URL('../public/env.js', import.meta.url), contents);
console.log(`[env] apiUrl = ${apiUrl}`);
