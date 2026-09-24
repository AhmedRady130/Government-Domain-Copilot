import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { ApiKeyService } from './api-key.service';
export const apiAuthInterceptor: HttpInterceptorFn = (request, next) => {
  const key = inject(ApiKeyService).key();
  return next(key ? request.clone({ setHeaders: { 'X-API-Key': key } }) : request);
};
export const apiErrorInterceptor: HttpInterceptorFn = (request, next) => next(request).pipe(catchError((error: HttpErrorResponse) => {
  const messages: Record<number, string> = { 400: 'Please check the information and try again.', 401: 'Sign in is required to continue.', 403: 'You are not permitted to perform this action.', 404: 'The requested item is unavailable for your tenant.', 409: 'This item changed. Refresh before trying again.', 413: 'The submitted content is too large.', 429: 'Too many requests. Please wait before trying again.', 500: 'The service could not complete that request.', 503: 'The service is temporarily unavailable.' };
  return throwError(() => new Error(messages[error.status] ?? (error.status ? 'The request could not be completed.' : 'The service could not be reached.')));
}));
