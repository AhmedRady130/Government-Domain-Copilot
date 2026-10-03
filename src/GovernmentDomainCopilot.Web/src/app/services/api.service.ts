import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApprovalDecision, ApprovalExecution, CreateSessionRequest, DocumentDetail, GroundedAnswerRequest, GroundedAnswerResponse, HealthResponse, LlmTrace, OrchestrationRequest, OrchestrationResponse, PendingApproval, PostSessionMessageRequest, RunDetail, RunSummary, SearchResponse, Session, SessionMessage } from '../models/api.models';
@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly root = environment.apiUrl.replace(/\/$/, '');
  constructor(private readonly http: HttpClient) {}
  health(): Observable<HealthResponse> { return this.http.get<HealthResponse>(`${this.root}/healthz`); }
  ready(): Observable<HealthResponse> { return this.http.get<HealthResponse>(`${this.root}/ready`); }
  search(query: string, topK = 5): Observable<SearchResponse> { return this.http.get<SearchResponse>(`${this.root}/api/search`, { params: new HttpParams().set('query', query).set('topK', String(topK)) }); }
  answer(request: GroundedAnswerRequest): Observable<GroundedAnswerResponse> { return this.http.post<GroundedAnswerResponse>(`${this.root}/api/answer`, request); }
  orchestrate(request: OrchestrationRequest): Observable<OrchestrationResponse> { return this.http.post<OrchestrationResponse>(`${this.root}/api/orchestrate`, request); }
  createSession(request: CreateSessionRequest): Observable<Session> { return this.http.post<Session>(`${this.root}/api/sessions`, request); }
  sessions(): Observable<Session[]> { return this.http.get<Session[]>(`${this.root}/api/sessions`); }
  session(id: string): Observable<Session> { return this.http.get<Session>(`${this.root}/api/sessions/${encodeURIComponent(id)}`); }
  sessionMessages(id: string): Observable<SessionMessage[]> { return this.http.get<SessionMessage[]>(`${this.root}/api/sessions/${encodeURIComponent(id)}/messages`); }
  postMessage(id: string, request: PostSessionMessageRequest): Observable<SessionMessage> { return this.http.post<SessionMessage>(`${this.root}/api/sessions/${encodeURIComponent(id)}/messages`, request); }
  runs(): Observable<RunSummary[]> { return this.http.get<RunSummary[]>(`${this.root}/api/runs`, { params: { skip: 0, take: 50 } }); }
  run(id: string): Observable<RunDetail> { return this.http.get<RunDetail>(`${this.root}/api/runs/${encodeURIComponent(id)}`); }
  approvals(): Observable<PendingApproval[]> { return this.http.get<PendingApproval[]>(`${this.root}/api/approvals`); }
  approval(id: string): Observable<PendingApproval> { return this.http.get<PendingApproval>(`${this.root}/api/approvals/${encodeURIComponent(id)}`); }
  decide(id: string, request: ApprovalDecision): Observable<PendingApproval> { return this.http.post<PendingApproval>(`${this.root}/api/approvals/${encodeURIComponent(id)}/decide`, request); }
  execute(id: string): Observable<ApprovalExecution> { return this.http.post<ApprovalExecution>(`${this.root}/api/approvals/${encodeURIComponent(id)}/execute`, {}); }
  traces(filter: { runId?: string; correlationId?: string }): Observable<LlmTrace[]> { return this.http.get<LlmTrace[]>(`${this.root}/api/traces/llm`, { params: filter }); }
}
