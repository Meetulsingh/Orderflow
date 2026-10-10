declare global {
  interface Window {
    __env?: { apiUrl?: string };
  }
}

/** Base URL of the API gateway, injected at build/deploy time through env.js. */
export const API_URL = (window.__env?.apiUrl ?? 'http://localhost:5000').replace(/\/+$/, '');
