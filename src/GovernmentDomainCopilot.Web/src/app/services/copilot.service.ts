import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiKeyService } from '../core/api-key.service';
import { OrchestrationRequest } from '../models/api.models';
export interface StreamEvent { runId: string; correlationId: string; eventType: string; stage: string; status: string; agentRole?: string; toolName?: string; message?: string; chunk?: string; errorMessage?: string; approvalRequestId?: string; approvalAction?: string; finalResponse?: { answer?: string; reason?: string; citations?: unknown[]; status?: string }; }
@Injectable({ providedIn: 'root' })
export class CopilotService {
  private readonly apiKey = inject(ApiKeyService);
  private readonly root = environment.apiUrl.replace(/\/$/, '');
  stream(request: OrchestrationRequest): Observable<StreamEvent> {
    return new Observable(observer => {
      const controller = new AbortController();
      fetch(`${this.root}/api/orchestrate/stream`, { method: 'POST', signal: controller.signal, headers: { 'Content-Type': 'application/json', 'X-API-Key': this.apiKey.key() }, body: JSON.stringify(request) })
        .then(async response => {
          if (!response.ok || !response.body) throw new Error(`${response.status}: ${response.status === 401 ? 'Sign in is required.' : response.status === 403 ? 'You are not permitted to perform this action.' : response.status === 429 ? 'Too many requests. Please wait and try again.' : 'The live workflow could not be started.'}`);
          const reader = response.body.getReader(), decoder = new TextDecoder(); let buffer = '';
          while (true) { const { done, value } = await reader.read(); if (done) break; buffer += decoder.decode(value, { stream: true }); const messages = buffer.split('\n\n'); buffer = messages.pop() ?? ''; for (const message of messages) { const data = message.split('\n').find(line => line.startsWith('data: ')); if (data) { try { observer.next(JSON.parse(data.substring(6)) as StreamEvent); } catch { /* malformed events are ignored; they cannot be rendered safely */ } } } }
          observer.complete();
        }).catch(error => observer.error(error));
      return () => controller.abort();
    });
  }
}
