import { Injectable, signal } from '@angular/core';
import { environment } from '../../environments/environment';
@Injectable({ providedIn: 'root' })
export class ApiKeyService {
  readonly key = signal(environment.defaultApiKey);
  set(value: string): void { this.key.set(value.trim()); }
}
