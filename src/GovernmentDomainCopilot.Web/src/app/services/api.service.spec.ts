import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { ApiService } from './api.service';

describe('ApiService', () => {
  let service: ApiService;
  let requests: HttpTestingController;
  beforeEach(() => { TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] }); service = TestBed.inject(ApiService); requests = TestBed.inject(HttpTestingController); });
  afterEach(() => requests.verify());

  it('posts session messages using the existing answer contract', () => {
    service.postMessage('session/a', { content: 'What documents are required?', mode: 'answer' }).subscribe();
    const request = requests.expectOne('/api/sessions/session%2Fa/messages');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ content: 'What documents are required?', mode: 'answer' });
    request.flush({ messageId: 'm1', sessionId: 'session/a', role: 'assistant', content: 'Answer', status: 'Completed', citations: [], timestamp: '2026-01-01T00:00:00Z' });
  });

  it('loads only the requested session history endpoint', () => {
    service.sessionMessages('tenant-session').subscribe();
    requests.expectOne('/api/sessions/tenant-session/messages').flush([]);
  });
});
