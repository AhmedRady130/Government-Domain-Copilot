export interface HealthResponse { status: string; }
export interface Citation { citationId: string; chunkId: string; documentId: string; sourceReference: string; title: string; sequence: number; }
export interface IngestDocumentRequest { title: string; sourceReference: string; sourceText: string; }
export interface IngestDocumentResponse { documentId: string; chunkCount: number; status: string; }
export interface DocumentDetail { documentId: string; title: string; sourceReference: string; status: string; chunkCount: number; failureReason?: string; createdAtUtc: string; }
export interface GroundedAnswerRequest { query: string; topK?: number; sessionId?: string; }
export interface GroundedAnswerResponse { status: string; answer?: string; reason?: string; citations: Citation[]; providerName: string; modelName: string; durationMs: number; sessionId?: string; }
export interface SearchItem { chunkId: string; documentId: string; sequence: number; title: string; sourceReference: string; content: string; distance?: number; keywordScore?: number; rrfScore: number; rank: number; rerankScore: number; finalRank: number; }
export interface SearchResponse { topK: number; totalReturned: number; durationMs: number; providerName: string; modelName: string; items: SearchItem[]; }
export interface CreateSessionRequest { title?: string; }
export interface Session { sessionId: string; tenantId: string; title: string; createdAt: string; lastActivityAt: string; status: string; }
export interface SessionMessage { messageId: string; sessionId: string; role: string; content: string; status: string; citations: Citation[]; linkedRunId?: string; timestamp: string; }
export interface PostSessionMessageRequest { content: string; mode?: 'answer' | 'orchestrate'; correlationId?: string; }
export interface OrchestrationRequest { query: string; correlationId?: string; sessionId?: string; }
export interface AgentExecution { agentRole: string; startedAt: string; completedAt: string; durationMs: number; success: boolean; outputSummary: string; errorMessage?: string; }
export interface PendingApproval { requestId: string; tenantId: string; proposedAction: string; originalPayload: string; editedPayload?: string; decision: string; reviewerComments?: string; createdAt: string; decidedAt?: string; isExecuted: boolean; executedAt?: string; }
export interface OrchestrationResponse { runId: string; correlationId: string; tenantId: string; patternName: string; status: string; iterationCount: number; durationMs: number; usedFallback: boolean; fallbackReason?: string; answer?: string; citations: Citation[]; agentExecutions: AgentExecution[]; pendingApproval?: PendingApproval; failureReason?: string; sessionId?: string; }
export interface ApprovalDecision { decision: 'Approved' | 'Rejected' | 'Edited'; comments?: string; editedPayload?: string; }
export interface ApprovalExecution { requestId: string; success: boolean; status: string; message: string; executedAt?: string; }
export interface RunSummary { runId: string; correlationId: string; tenantId: string; patternName: string; status: string; iterationCount: number; durationMs: number; usedFallback: boolean; fallbackReason?: string; startedAt: string; completedAt: string; sessionId?: string; }
export interface RunDetail extends RunSummary { answer?: string; citations: Citation[]; agentExecutions: AgentExecution[]; pendingApproval?: PendingApproval; failureReason?: string; }
export interface LlmTrace { id: string; tenantId: string; correlationId: string; runId?: string; providerName: string; modelName: string; operationType: string; startedAt: string; completedAt: string; durationMs: number; isSuccess: boolean; promptTokens?: number; completionTokens?: number; totalTokens?: number; estimatedCost?: number; errorMessage?: string; }
