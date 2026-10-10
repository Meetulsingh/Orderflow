import { HttpContextToken } from '@angular/common/http';

/** Marks requests that must not carry a bearer token or trigger a refresh (login, register, refresh). */
export const SKIP_AUTH = new HttpContextToken<boolean>(() => false);

/** Opt-out of global error toasts for callers that render errors inline or poll silently. */
export const SILENT_ERRORS = new HttpContextToken<boolean>(() => false);
