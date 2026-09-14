using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GovernmentDomainCopilot.Application.Agents.Models;
using GovernmentDomainCopilot.Application.Answering.Models;
using GovernmentDomainCopilot.Domain.Entities;
using GovernmentDomainCopilot.Infrastructure.Persistence;
using GovernmentDomainCopilot.Infrastructure.Sessions;
using GovernmentDomainCopilot.Infrastructure.Traces;
using Microsoft.Extensions.Options;
using Xunit;

namespace Integration.Tests;

public sealed class PostgresSessionAndRunTracePersistenceIntegrationTests : IClassFixture<PostgreSqlTestDatabaseFixture>
{
    private readonly PostgreSqlTestDatabaseFixture _fixture;

    public PostgresSessionAndRunTracePersistenceIntegrationTests(PostgreSqlTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private static async Task<Tenant> CreateTenantAsync(GovernmentDomainCopilotDbContext context, string? name = null)
    {
        var tenant = new Tenant(Guid.NewGuid(), name ?? "Session Test Tenant", DateTimeOffset.UtcNow);
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();
        return tenant;
    }

    [Fact]
    public async Task Session_and_messages_persist_across_context_recreation()
    {
        var tenantId = Guid.Empty;
        string sessionId = string.Empty;

        // Step 1: Create session & append messages in Scope 1
        using (var context1 = _fixture.CreateDbContext())
        {
            var tenant = await CreateTenantAsync(context1, "Durability Tenant");
            tenantId = tenant.Id;
            var store1 = new PostgresSessionStore(context1);

            var session = await store1.CreateSessionAsync(tenantId, "Passport Inquiry");
            sessionId = session.SessionId;

            await store1.AppendMessageAsync(sessionId, tenantId, "user", "How do I renew my passport?", "UserQuery");

            var citations = new List<CitationItem>
            {
                new("cit-1", Guid.NewGuid(), Guid.NewGuid(), "law-2026", "Passport Guidelines", 1)
            };
            await store1.AppendMessageAsync(sessionId, tenantId, "assistant", "Submit Form DS-11 and fee $130.", "Completed", citations, "run-xyz-123");
        }

        // Step 2: Fresh context (Scope 2) simulating process restart / replica access
        using (var context2 = _fixture.CreateDbContext())
        {
            var store2 = new PostgresSessionStore(context2);

            var retrievedSession = await store2.GetSessionAsync(sessionId, tenantId);
            Assert.NotNull(retrievedSession);
            Assert.Equal("Passport Inquiry", retrievedSession.Title);
            Assert.Equal(tenantId, retrievedSession.TenantId);

            var messages = await store2.GetMessagesAsync(sessionId, tenantId);
            Assert.NotNull(messages);
            Assert.Equal(2, messages!.Count);

            Assert.Equal("user", messages[0].Role);
            Assert.Equal("How do I renew my passport?", messages[0].Content);

            Assert.Equal("assistant", messages[1].Role);
            Assert.Equal("Submit Form DS-11 and fee $130.", messages[1].Content);
            Assert.Equal("run-xyz-123", messages[1].LinkedRunId);
            Assert.Single(messages[1].Citations);
            Assert.Equal("Passport Guidelines", messages[1].Citations[0].Title);
            Assert.Equal("law-2026", messages[1].Citations[0].SourceReference);
        }
    }

    [Fact]
    public async Task Run_trace_persists_across_context_recreation()
    {
        var tenantId = Guid.Empty;
        var runId = $"run-{Guid.NewGuid():N}";

        // Step 1: Record run in Scope 1
        using (var context1 = _fixture.CreateDbContext())
        {
            var tenant = await CreateTenantAsync(context1, "Run Durability Tenant");
            tenantId = tenant.Id;
            var traceStore1 = new PostgresRunTraceStore(context1);

            var record = new OrchestrationRunRecord(
                RunId: runId,
                CorrelationId: "corr-trace-1",
                TenantId: tenantId,
                PatternName: "Sequential Pipeline",
                StartedAt: DateTimeOffset.UtcNow.AddSeconds(-2),
                CompletedAt: DateTimeOffset.UtcNow,
                Duration: TimeSpan.FromSeconds(2),
                Status: "Completed",
                IterationCount: 3,
                AgentExecutions: new List<AgentExecutionRecord>
                {
                    new("Eligibility Identifier Agent", DateTimeOffset.UtcNow.AddSeconds(-2), DateTimeOffset.UtcNow.AddSeconds(-1), TimeSpan.FromSeconds(1), true, Array.Empty<AgentToolCallRecord>(), "Eligible"),
                    new("Procedure Resolver Agent", DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1), true, Array.Empty<AgentToolCallRecord>(), "Procedure resolved")
                },
                UsedFallback: false,
                FallbackReason: null,
                FinalResponse: new GroundedAnswerResponse(
                    GroundedAnswerStatus.Grounded,
                    "You qualify for fee waiver.",
                    null,
                    new List<CitationItem> { new("c-1", Guid.NewGuid(), Guid.NewGuid(), "ref-waiver", "Fee Waiver Policy", 1) },
                    "Gemini",
                    "gemini-2.5-flash",
                    TimeSpan.FromMilliseconds(500)),
                PendingApproval: null,
                FailureReason: null,
                SessionId: "sess-run-link");

            await traceStore1.RecordRunAsync(record, "sess-run-link");
        }

        // Step 2: Fresh context (Scope 2)
        using (var context2 = _fixture.CreateDbContext())
        {
            var traceStore2 = new PostgresRunTraceStore(context2);

            var retrievedRun = await traceStore2.GetRunAsync(runId, tenantId);
            Assert.NotNull(retrievedRun);
            Assert.Equal(runId, retrievedRun.RunId);
            Assert.Equal("corr-trace-1", retrievedRun.CorrelationId);
            Assert.Equal("sess-run-link", retrievedRun.SessionId);
            Assert.Equal("Completed", retrievedRun.Status);
            Assert.Equal(3, retrievedRun.IterationCount);
            Assert.Equal(2, retrievedRun.AgentExecutions.Count);
            Assert.Equal("Eligibility Identifier Agent", retrievedRun.AgentExecutions[0].AgentRole);
            Assert.NotNull(retrievedRun.FinalResponse);
            Assert.Equal("You qualify for fee waiver.", retrievedRun.FinalResponse!.Answer);
            Assert.Single(retrievedRun.FinalResponse.Citations);
        }
    }

    [Fact]
    public async Task Tenant_isolation_prevents_cross_tenant_access_and_appends()
    {
        using var context = _fixture.CreateDbContext();
        var tenantA = await CreateTenantAsync(context, "Tenant A");
        var tenantB = await CreateTenantAsync(context, "Tenant B");

        var sessionStore = new PostgresSessionStore(context);
        var traceStore = new PostgresRunTraceStore(context);

        // Tenant A creates session & records run
        var sessionA = await sessionStore.CreateSessionAsync(tenantA.Id, "Tenant A Confidential Session");
        var runA = new OrchestrationRunRecord(
            RunId: $"run-{Guid.NewGuid():N}",
            CorrelationId: "corr-a",
            TenantId: tenantA.Id,
            PatternName: "Sequential Pipeline",
            StartedAt: DateTimeOffset.UtcNow,
            CompletedAt: DateTimeOffset.UtcNow,
            Duration: TimeSpan.Zero,
            Status: "Completed",
            IterationCount: 1,
            AgentExecutions: Array.Empty<AgentExecutionRecord>(),
            UsedFallback: false,
            FallbackReason: null,
            FinalResponse: null,
            PendingApproval: null,
            SessionId: sessionA.SessionId);

        await traceStore.RecordRunAsync(runA, sessionA.SessionId);

        // Tenant B attempts to read Tenant A's session
        var sessionForB = await sessionStore.GetSessionAsync(sessionA.SessionId, tenantB.Id);
        Assert.Null(sessionForB);

        // Tenant B attempts to get Tenant A's session messages
        var messagesForB = await sessionStore.GetMessagesAsync(sessionA.SessionId, tenantB.Id);
        Assert.Null(messagesForB);

        // Tenant B attempts to append to Tenant A's session
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            sessionStore.AppendMessageAsync(sessionA.SessionId, tenantB.Id, "user", "Malicious cross-tenant probe"));

        // Tenant B attempts to read Tenant A's run trace
        var runForB = await traceStore.GetRunAsync(runA.RunId, tenantB.Id);
        Assert.Null(runForB);

        // Tenant B lists runs — should not see Tenant A's run
        var tenantBRuns = await traceStore.ListRunsAsync(tenantB.Id);
        Assert.DoesNotContain(tenantBRuns, r => r.RunId == runA.RunId);

        // Tenant B lists sessions — should not see Tenant A's session
        var tenantBSessions = await sessionStore.ListSessionsAsync(tenantB.Id);
        Assert.DoesNotContain(tenantBSessions, s => s.SessionId == sessionA.SessionId);
    }

    [Fact]
    public async Task Pagination_and_ordering_are_deterministic()
    {
        using var context = _fixture.CreateDbContext();
        var tenant = await CreateTenantAsync(context, "Pagination Tenant");
        var sessionStore = new PostgresSessionStore(context);
        var traceStore = new PostgresRunTraceStore(context);

        // Create multiple sessions with distinct activity times
        var baseTime = DateTimeOffset.UtcNow;
        for (int i = 0; i < 5; i++)
        {
            var s = await sessionStore.CreateSessionAsync(tenant.Id, $"Session {i}");
            await sessionStore.AppendMessageAsync(s.SessionId, tenant.Id, "user", $"Message in session {i}");
            await Task.Delay(10); // Ensure timestamp divergence
        }

        var page1 = await sessionStore.ListSessionsAsync(tenant.Id, skip: 0, take: 2);
        var page2 = await sessionStore.ListSessionsAsync(tenant.Id, skip: 2, take: 2);

        Assert.Equal(2, page1.Count);
        Assert.Equal(2, page2.Count);
        Assert.NotEqual(page1[0].SessionId, page2[0].SessionId);
        Assert.True(page1[0].LastActivityAt >= page1[1].LastActivityAt);
        Assert.True(page1[1].LastActivityAt >= page2[0].LastActivityAt);

        // Create multiple runs
        for (int i = 0; i < 5; i++)
        {
            var runRecord = new OrchestrationRunRecord(
                RunId: $"run-page-{i}",
                CorrelationId: $"corr-{i}",
                TenantId: tenant.Id,
                PatternName: "Sequential Pipeline",
                StartedAt: baseTime.AddSeconds(i),
                CompletedAt: baseTime.AddSeconds(i + 1),
                Duration: TimeSpan.FromSeconds(1),
                Status: "Completed",
                IterationCount: 1,
                AgentExecutions: Array.Empty<AgentExecutionRecord>(),
                UsedFallback: false,
                FallbackReason: null,
                FinalResponse: null,
                PendingApproval: null);

            await traceStore.RecordRunAsync(runRecord);
        }

        var runsPage1 = await traceStore.ListRunsAsync(tenant.Id, skip: 0, take: 2);
        var runsPage2 = await traceStore.ListRunsAsync(tenant.Id, skip: 2, take: 2);

        Assert.Equal(2, runsPage1.Count);
        Assert.Equal(2, runsPage2.Count);
        Assert.Equal("run-page-4", runsPage1[0].RunId); // Most recent first
        Assert.Equal("run-page-3", runsPage1[1].RunId);
        Assert.Equal("run-page-2", runsPage2[0].RunId);
    }

    [Fact]
    public async Task Over_limit_message_content_is_rejected()
    {
        using var context = _fixture.CreateDbContext();
        var tenant = await CreateTenantAsync(context, "Validation Tenant");
        var sessionStore = new PostgresSessionStore(context, Options.Create(new GovernmentDomainCopilot.Application.Sessions.Models.SessionOptions
        {
            MaxMessageContentLength = 100
        }));

        var session = await sessionStore.CreateSessionAsync(tenant.Id, "Short Limit Session");

        var oversizedContent = new string('A', 101);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            sessionStore.AppendMessageAsync(session.SessionId, tenant.Id, "user", oversizedContent));
    }
}
