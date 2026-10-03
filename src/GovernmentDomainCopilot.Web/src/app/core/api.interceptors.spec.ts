import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ApiKeyService } from './api-key.service';
import { apiAuthInterceptor, apiErrorInterceptor } from './api.interceptors';

describe('API interceptors', () => {
  let http: HttpClient;
  let requests: HttpTestingController;
  let keys: ApiKeyService;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(withInterceptors([apiAuthInterceptor, apiErrorInterceptor])), provideHttpClientTesting()] });
    http = TestBed.inject(HttpClient); requests = TestBed.inject(HttpTestingController); keys = TestBed.inject(ApiKeyService);
  });
  afterEach(() => requests.verify());

  it('adds only the current in-memory API key to requests', () => {
    keys.set(' synthetic-key '); http.get('/api/sessions').subscribe();
    const request = requests.expectOne('/api/sessions');
    expect(request.request.headers.get('X-API-Key')).toBe('synthetic-key'); request.flush([]);
  });

  it('does not add an auth header after the in-memory key is cleared', () => {
    keys.set(''); http.get('/api/sessions').subscribe();
    const request = requests.expectOne('/api/sessions');
    expect(request.request.headers.has('X-API-Key')).toBeFalse(); request.flush([]);
  });

  const statusMessages: ReadonlyArray<readonly [number, string]> = [
    [400, 'Please check the information and try again.'], [403, 'You are not permitted to perform this action.'],
    [413, 'The submitted content is too large.'], [429, 'Too many requests. Please wait before trying again.']
  ];
  statusMessages.forEach(([status, message]) => it(`maps ${status} responses to a safe user message`, () => {
    let received = ''; http.get('/api/test').subscribe({ error: error => received = error.message });
    requests.expectOne('/api/test').flush({}, { status, statusText: 'Error' });
    expect(received).toBe(message);
  }));
});
