import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { SKIP_AUTH } from '../http-context';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { API_URL } from '../config';
import { AuthService } from './auth.service';


const withToken = (req: HttpRequest<unknown>, token: string) =>
  req.clone({ setHeaders: { Authorization: `Bearer ${token}` } });

/**
 * Attaches the JWT to gateway calls. On 401 it rotates the refresh token once and replays
 * the request; if refreshing fails the session is expired and the user is sent to login.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);

  if (!req.url.startsWith(API_URL) || req.context.get(SKIP_AUTH)) return next(req);

  const token = auth.accessToken;
  const outgoing = token ? withToken(req, token) : req;

  return next(outgoing).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401 || !token) {
        return throwError(() => error);
      }
      return auth.refresh().pipe(
        catchError(refreshError => {
          auth.expireSession();
          return throwError(() => refreshError);
        }),
        switchMap(newToken => next(withToken(req, newToken)))
      );
    })
  );
};
