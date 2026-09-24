import { TestBed } from '@angular/core/testing';
import { ApiKeyService } from '../core/api-key.service';
import { CopilotService, StreamEvent } from './copilot.service';

describe('CopilotService streaming', () => {
  let service: CopilotService;
  beforeEach(() => { TestBed.configureTestingModule({ providers: [CopilotService, ApiKeyService] }); service = TestBed.inject(CopilotService); TestBed.inject(ApiKeyService).set('stream-key'); });

  it('posts the documented request and parses complete SSE events deterministically', done => {
    const encoder = new TextEncoder();
    const body = new ReadableStream<Uint8Array>({ start(controller) { controller.enqueue(encoder.encode('event: AgentProgress\ndata: {"runId":"r1","correlationId":"c1","eventType":"AgentProgress","stage":"Eligibility","status":"Running","agentRole":"EligibilityIdentifierAgent"}\n\n')); controller.enqueue(encoder.encode('event: RunCompleted\ndata: {"runId":"r1","correlationId":"c1","eventType":"RunCompleted","stage":"Response","status":"Completed"}\n\n')); controller.close(); } });
    const fetchSpy = spyOn(window, 'fetch').and.returnValue(Promise.resolve(new Response(body, { status: 200 })));
    const events: StreamEvent[] = [];
    service.stream({ query: 'Need help', sessionId: 's1' }).subscribe({ next: event => events.push(event), complete: () => {
      expect(fetchSpy).toHaveBeenCalledWith('/api/orchestrate/stream', jasmine.objectContaining({ method: 'POST', headers: jasmine.objectContaining({ 'X-API-Key': 'stream-key' }), body: JSON.stringify({ query: 'Need help', sessionId: 's1' }) }));
      expect(events.map(event => event.eventType)).toEqual(['AgentProgress', 'RunCompleted']); done();
    }});
  });

  it('ignores malformed SSE data rather than rendering it as answer content', done => {
    const body = new ReadableStream<Uint8Array>({ start(controller) { controller.enqueue(new TextEncoder().encode('data: not-json\n\n')); controller.close(); } });
    spyOn(window, 'fetch').and.returnValue(Promise.resolve(new Response(body, { status: 200 })));
    service.stream({ query: 'Question' }).subscribe({ next: () => fail('malformed event should not be emitted'), complete: done });
  });
});
