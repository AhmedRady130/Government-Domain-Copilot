using System;
using System.Linq;
using System.Threading.Tasks;
using GovernmentDomainCopilot.Application.Sessions.Models;
using GovernmentDomainCopilot.Application.Sessions.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace Application.Tests.Sessions;

public sealed class InMemorySessionStoreTests
{
    [Fact]
    public async Task CreateSessionAsync_And_GetSessionAsync_Succeeds()
    {
        var store = new InMemorySessionStore();
        var tenantId = Guid.NewGuid();

        var session = await store.CreateSessionAsync(tenantId, "My Test Session");

        Assert.NotNull(session);
        Assert.Equal("My Test Session", session.Title);
        Assert.Equal(tenantId, session.TenantId);
        Assert.Equal("Active", session.Status);

        var retrieved = await store.GetSessionAsync(session.SessionId, tenantId);
        Assert.NotNull(retrieved);
        Assert.Equal(session.SessionId, retrieved.SessionId);
        Assert.Equal("My Test Session", retrieved.Title);
    }

    [Fact]
    public async Task GetSessionAsync_CrossTenant_ReturnsNull()
    {
        var store = new InMemorySessionStore();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var session = await store.CreateSessionAsync(tenantA, "Secret Session");
        var crossTenant = await store.GetSessionAsync(session.SessionId, tenantB);

        Assert.Null(crossTenant);
    }

    [Fact]
    public async Task ListSessionsAsync_IsTenantIsolated_And_SortedDescendingByLastActivityAt()
    {
        var store = new InMemorySessionStore();
        var tenant1 = Guid.NewGuid();
        var tenant2 = Guid.NewGuid();

        var s1 = await store.CreateSessionAsync(tenant1, "S1");
        await Task.Delay(20);
        var s2 = await store.CreateSessionAsync(tenant1, "S2");
        await Task.Delay(20);
        var sOther = await store.CreateSessionAsync(tenant2, "SOther");

        var list1 = (await store.ListSessionsAsync(tenant1)).ToList();
        var list2 = (await store.ListSessionsAsync(tenant2)).ToList();

        Assert.Equal(2, list1.Count);
        // Most recently created/touched should appear first
        Assert.Equal(s2.SessionId, list1[0].SessionId);
        Assert.Equal(s1.SessionId, list1[1].SessionId);

        Assert.Single(list2);
        Assert.Equal(sOther.SessionId, list2[0].SessionId);
    }

    [Fact]
    public async Task AppendMessageAsync_PreservesOrderAndFields()
    {
        var store = new InMemorySessionStore();
        var tenantId = Guid.NewGuid();
        var session = await store.CreateSessionAsync(tenantId, "Order Test");

        await store.AppendMessageAsync(session.SessionId, tenantId, "user", "Hello", "UserQuery");
        await store.AppendMessageAsync(
            session.SessionId, tenantId, "assistant",
            "Welcome! According to law [1], here is your info.",
            "Grounded",
            citations: new[] { new GovernmentDomainCopilot.Application.Answering.Models.CitationItem("1", Guid.NewGuid(), Guid.NewGuid(), "ref.pdf", "Policy", 1) },
            linkedRunId: "run-001");

        var messages = await store.GetMessagesAsync(session.SessionId, tenantId);

        Assert.NotNull(messages);
        Assert.Equal(2, messages!.Count);
        Assert.Equal("user", messages[0].Role);
        Assert.Equal("Hello", messages[0].Content);

        Assert.Equal("assistant", messages[1].Role);
        Assert.Equal("run-001", messages[1].LinkedRunId);
        Assert.Equal("Grounded", messages[1].Status);
        Assert.Single(messages[1].Citations);
    }

    [Fact]
    public async Task AppendMessageAsync_NonexistentSession_ThrowsKeyNotFoundException()
    {
        var store = new InMemorySessionStore();
        var tenant = Guid.NewGuid();

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            store.AppendMessageAsync("sess-doesnotexist", tenant, "user", "hi"));
    }

    [Fact]
    public async Task AppendMessageAsync_CrossTenant_ThrowsKeyNotFoundException()
    {
        var store = new InMemorySessionStore();
        var tenant1 = Guid.NewGuid();
        var tenant2 = Guid.NewGuid();

        var session = await store.CreateSessionAsync(tenant1, "Tenant1 Session");

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            store.AppendMessageAsync(session.SessionId, tenant2, "user", "hi"));
    }

    [Fact]
    public async Task AppendMessageAsync_ExceedingMaxLength_ThrowsArgumentException()
    {
        var options = Options.Create(new SessionOptions { MaxMessageContentLength = 50 });
        var store = new InMemorySessionStore(options);
        var tenantId = Guid.NewGuid();
        var session = await store.CreateSessionAsync(tenantId, "Length Test");

        var longContent = new string('x', 51);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            store.AppendMessageAsync(session.SessionId, tenantId, "user", longContent));

        Assert.Contains("exceeds the maximum allowed length", ex.Message);
    }

    [Fact]
    public async Task GetMessagesAsync_CrossTenant_ReturnsNull()
    {
        var store = new InMemorySessionStore();
        var tenant1 = Guid.NewGuid();
        var tenant2 = Guid.NewGuid();

        var session = await store.CreateSessionAsync(tenant1, "Session");
        await store.AppendMessageAsync(session.SessionId, tenant1, "user", "Hello");

        var messages = await store.GetMessagesAsync(session.SessionId, tenant2);
        Assert.Null(messages);
    }
}
