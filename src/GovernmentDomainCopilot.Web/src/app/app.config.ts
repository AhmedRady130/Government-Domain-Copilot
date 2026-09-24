import { ApplicationConfig } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { apiAuthInterceptor, apiErrorInterceptor } from './core/api.interceptors';
import { routes } from './app.routes';
export const appConfig: ApplicationConfig = { providers: [provideRouter(routes), provideHttpClient(withInterceptors([apiAuthInterceptor, apiErrorInterceptor]))] };
