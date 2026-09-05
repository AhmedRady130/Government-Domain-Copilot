using GovernmentDomainCopilot.Domain.Entities;

namespace Domain.Tests;

public sealed class TenantOwnershipTests
{
    [Fact]
    public void Document_preserves_its_explicit_tenant_identifier()
    {
        var tenantId = Guid.NewGuid();
        var document = new Document(
            Guid.NewGuid(),
            tenantId,
            "Document title",
            "source-reference",
            DateTimeOffset.UtcNow);

        Assert.Equal(tenantId, document.TenantId);
    }

    [Fact]
    public void Tenant_owned_entities_reject_an_empty_tenant_identifier()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Document(
            Guid.NewGuid(),
            Guid.Empty,
            "Document title",
            "source-reference",
            DateTimeOffset.UtcNow));
    }
}
