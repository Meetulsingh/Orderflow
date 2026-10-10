import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { SILENT_ERRORS } from './http-context';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { ProblemDetails } from './models';
import { ToastService } from './toast.service';


export function problemMessage(error: unknown): string {
  if (!(error instanceof HttpErrorResponse)) return 'Something went wrong.';
  if (error.status === 0) return 'Cannot reach the server. Services on the free tier may still be waking up — try again in a few seconds.';
  const problem = error.error as ProblemDetails | null;
  if (problem?.errors) {
    const first = Object.values(problem.errors)[0]?.[0];
    if (first) return first;
  }
  return problem?.detail || problem?.title || error.message;
}

/** Global HTTP error handling: converts RFC 9457 problem details into toasts. */
export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const toast = inject(ToastService);
  return next(req).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && !req.context.get(SILENT_ERRORS) && error.status !== 401) {
        const title =
          error.status === 0 ? 'Connection problem'
          : error.status === 429 ? 'Slow down'
          : error.status === 403 ? 'Not allowed'
          : error.status >= 500 ? 'Server error'
          : 'Request failed';
        toast.error(title, problemMessage(error));
      }
      return throwError(() => error);
    })
  );
};
