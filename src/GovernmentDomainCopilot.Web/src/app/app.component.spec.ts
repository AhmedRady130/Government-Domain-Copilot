import { ComponentFixture, TestBed } from '@angular/core/testing';
import { EMPTY, of } from 'rxjs';
import { AppComponent } from './app.component';
import { ApiKeyService } from './core/api-key.service';
import { ApiService } from './services/api.service';
import { CopilotService } from './services/copilot.service';
import { DocumentService } from './services/document.service';
import { ApprovalExecution, PendingApproval, RunDetail, SearchResponse, Session } from './models/api.models';

describe('AppComponent', () => {
  let fixture: ComponentFixture<AppComponent>;
  let component: AppComponent;
  let api: jasmine.SpyObj<ApiService>;

  beforeEach(async () => {
    api = jasmine.createSpyObj<ApiService>('ApiService', ['health', 'createSession', 'sessions', 'sessionMessages', 'approvals', 'runs', 'run', 'traces', 'postMessage', 'decide', 'execute', 'search']);
    const session: Session = { sessionId: 'new-session', tenantId: 'tenant-a', title: 'New request', createdAt: '', lastActivityAt: '', status: 'Open' };
    const approval: PendingApproval = { requestId: 'a1', tenantId: 'tenant-a', proposedAction: 'Publish', originalPayload: 'Draft', decision: 'Pending', createdAt: '', isExecuted: false };
    const run: RunDetail = { runId: 'r1', correlationId: 'c1', tenantId: 'tenant-a', patternName: 'Sequential', status: 'Completed', iterationCount: 1, durationMs: 1, usedFallback: false, startedAt: '', completedAt: '', citations: [], agentExecutions: [] };
    const execution: ApprovalExecution = { requestId: 'a1', success: true, status: 'Executed', message: 'Executed' };
    const search: SearchResponse = { topK: 5, totalReturned: 0, durationMs: 1, providerName: 'provider', modelName: 'model', items: [] };
    api.health.and.returnValue(of({ status: 'Healthy' })); api.createSession.and.returnValue(of(session));
    api.sessions.and.returnValue(of([])); api.sessionMessages.and.returnValue(of([])); api.approvals.and.returnValue(of([])); api.runs.and.returnValue(of([])); api.run.and.returnValue(of(run)); api.traces.and.returnValue(of([]));
    api.postMessage.and.returnValue(of({ messageId: 'm1', sessionId: 's1', role: 'assistant', content: 'Answer', status: 'Completed', citations: [], timestamp: '2026-01-01T00:00:00Z' }));
    api.decide.and.returnValue(of(approval)); api.execute.and.returnValue(of(execution)); api.search.and.returnValue(of(search));
    await TestBed.configureTestingModule({ imports: [AppComponent], providers: [
      ApiKeyService, { provide: ApiService, useValue: api }, { provide: DocumentService, useValue: {} }, { provide: CopilotService, useValue: { stream: () => EMPTY } }
    ] }).compileComponents();
    fixture = TestBed.createComponent(AppComponent); component = fixture.componentInstance; fixture.detectChanges();
  });

  it('renders supplied citations and visibly marks controlled refusals', () => {
    component.view.set('copilot');
    component.messages.set([{ role: 'assistant', content: 'Evidence is insufficient.', status: 'Refused', citations: [{ citationId: 'c1', chunkId: 'chunk', documentId: 'document', title: 'Service guide', sourceReference: 'https://example.test/guide', sequence: 2 }] }]);
    fixture.detectChanges();
    const message = fixture.nativeElement.querySelector('.chat-log article');
    expect(message.classList).toContain('refusal');
    expect(message.textContent).toContain('Service guide');
    expect(message.querySelector('a').getAttribute('href')).toBe('https://example.test/guide');
  });

  it('loads the selected server-scoped session history without accepting a tenant override', () => {
    const history = [{ messageId: 'm1', sessionId: 's-a', role: 'assistant', content: 'Grounded response', status: 'Completed', citations: [], timestamp: '2026-01-01T00:00:00Z' }];
    api.sessionMessages.and.returnValue(of(history));
    component.openSession({ sessionId: 's-a', tenantId: 'tenant-a', title: 'Case A', createdAt: '2026-01-01T00:00:00Z', lastActivityAt: '2026-01-01T00:00:00Z', status: 'Open' });
    expect(api.sessionMessages).toHaveBeenCalledWith('s-a');
    expect(component.activeSession()).toBe('s-a');
    expect(component.history()).toEqual(history);
  });

  it('shows pending approval controls and keeps approved execution distinct', () => {
    component.view.set('approvals');
    component.approvals.set([{ requestId: 'a1', tenantId: 'tenant-a', proposedAction: 'Publish response', originalPayload: 'Draft', decision: 'Pending', createdAt: '2026-01-01T00:00:00Z', isExecuted: false }]);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Approve');
    expect(fixture.nativeElement.textContent).not.toContain('Execute approved action');
    component.approvals.set([{ requestId: 'a1', tenantId: 'tenant-a', proposedAction: 'Publish response', originalPayload: 'Draft', decision: 'Approved', createdAt: '2026-01-01T00:00:00Z', isExecuted: false }]);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Execute approved action');
  });

  it('clears tenant-scoped UI state when the in-memory identity changes', () => {
    component.activeSession.set('tenant-a-session'); component.sessions.set([{ sessionId: 'tenant-a-session', tenantId: 'tenant-a', title: 'A', createdAt: '', lastActivityAt: '', status: 'Open' }]);
    component.history.set([{ messageId: 'm1', sessionId: 'tenant-a-session', role: 'user', content: 'private', status: 'Submitted', citations: [], timestamp: '' }]);
    component.connectionKey = 'new-identity'; component.applyConnection();
    expect(component.activeSession()).toBeNull(); expect(component.sessions()).toEqual([]); expect(component.history()).toEqual([]);
  });
});
