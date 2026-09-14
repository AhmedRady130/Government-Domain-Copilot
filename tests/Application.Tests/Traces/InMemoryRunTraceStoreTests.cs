using System;
using System.Linq;
using System.Threading.Tasks;
using GovernmentDomainCopilot.Application.Agents.Models;
using GovernmentDomainCopilot.Application.Answering.Models;
using GovernmentDomainCopilot.Application.Traces.Services;
using Xunit;

namespace Application.Tests.Traces;

public sealed class InMemoryRunTraceStoreTests
{
    private static OrchestrationRunRecord MakeRecord(string runId, Guid tenantId, string? sessionId = null, DateTimeOffset? startedAt = null)
    {
        var now = startedAt ?? DateTimeOffset.UtcNow;
        return new OrchestrationRunRecord(
            RunId: runId,
            CorrelationId: $"corr-{runId}",
            TenantId: tenantId,
            PatternName: "Sequential Pipeline",
            StartedAt: now,
            CompletedAt: now.AddSeconds(1),
            Duration: TimeSpan.FromSeconds(1),
            Status: "Completed",
            IterationCount: 1,
            AgentExecutions: Array.Empty<AgentExecutionRecord>(),
            UsedFallback: false,
            FallbackReason: null,
            FinalResponse: null,
            PendingApproval: null,
            FailureReason: null,
            SessionId: sessionId);
    }

    [Fact]
    public async Task RecordRunAsync_And_GetRunAsync_StoresAndRetrievesSuccessfully()
    {
        var store = new InMemoryRunTraceStore();
        var tenantId = Guid.NewGuid();
        var record = MakeRecord("run-101", tenantId, sessionId: "sess-abc");

        await store.RecordRunAsync(record);
        var retrieved = await store.GetRunAsync("run-101", tenantId);

        Assert.NotNull(retrieved);
        Assert.Equal("run-101", retrieved.RunId);
        Assert.Equal(tenantId, retrieved.TenantId);
        Assert.Equal("Completed", retrieved.Status);
        Assert.Equal("sess-abc", retrieved.SessionId);
    }

    [Fact]
    public async Task GetRunAsync_CrossTenantAccess_ReturnsNull()
    {
        var store = new InMemoryRunTraceStore();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await store.RecordRunAsync(MakeRecord("run-secret", tenantA));

        var result = await store.GetRunAsync("run-secret", tenantB);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetRunAsync_NonExistentId_ReturnsNull()
    {
        var store = new InMemoryRunTraceStore();
        var result = await store.GetRunAsync("does-not-exist", Guid.NewGuid());
        Assert.Null(result);
    }

    [Fact]
    public async Task ListRunsAsync_IsTenantIsolated_And_SortedDescendingByStartedAt()
    {
        var store = new InMemoryRunTraceStore();
        var tenant1 = Guid.NewGuid();
        var tenant2 = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await store.RecordRunAsync(MakeRecord("run-1", tenant1, startedAt: now.AddMinutes(1)));
        await store.RecordRunAsync(MakeRecord("run-2", tenant1, startedAt: now.AddMinutes(3)));
        await store.RecordRunAsync(MakeRecord("run-3", tenant1, startedAt: now.AddMinutes(2)));
        await store.RecordRunAsync(MakeRecord("run-other", tenant2, startedAt: now.AddMinutes(4)));

        var tenant1Runs = (await store.ListRunsAsync(tenant1)).ToList();
        var tenant2Runs = (await store.ListRunsAsync(tenant2)).ToList();

        Assert.Equal(3, tenant1Runs.Count);
        Assert.DoesNotContain(tenant1Runs, r => r.RunId == "run-other");
        // Sorted descending: run-2 (now+3m), run-3 (now+2m), run-1 (now+1m)
        Assert.Equal("run-2", tenant1Runs[0].RunId);
        Assert.Equal("run-3", tenant1Runs[1].RunId);
        Assert.Equal("run-1", tenant1Runs[2].RunId);

        Assert.Single(tenant2Runs);
        Assert.Equal("run-other", tenant2Runs[0].RunId);
    }

    [Fact]
    public async Task ListRunsAsync_Pagination_HonorsSkipAndTake()
    {
        var store = new InMemoryRunTraceStore();
        var tenant = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        for (int i = 0; i < 15; i++)
        {
            await store.RecordRunAsync(MakeRecord($"run-{i:D2}", tenant, startedAt: now.AddMinutes(i)));
        }

        var page1 = (await store.ListRunsAsync(tenant, skip: 0, take: 5)).ToList();
        var page2 = (await store.ListRunsAsync(tenant, skip: 5, take: 5)).ToList();

        Assert.Equal(5, page1.Count);
        Assert.Equal(5, page2.Count);
        // page1 should have highest StartedAt first (run-14)
        Assert.Equal("run-14", page1[0].RunId);
        // page2 should start at run-09
        Assert.Equal("run-09", page2[0].RunId);
    }

    [Fact]
    public async Task ListRunsAsync_SessionFilter_ReturnsOnlySessionRuns()
    {
        var store = new InMemoryRunTraceStore();
        var tenant = Guid.NewGuid();

        await store.RecordRunAsync(MakeRecord("run-a", tenant, sessionId: "sess-1"));
        await store.RecordRunAsync(MakeRecord("run-b", tenant, sessionId: "sess-2"));
        await store.RecordRunAsync(MakeRecord("run-c", tenant, sessionId: "sess-1"));

        var filtered = (await store.ListRunsAsync(tenant, sessionId: "sess-1")).ToList();

        Assert.Equal(2, filtered.Count);
        Assert.All(filtered, r => Assert.Equal("sess-1", r.SessionId));
    }
}
