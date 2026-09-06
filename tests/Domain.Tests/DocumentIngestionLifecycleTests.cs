using GovernmentDomainCopilot.Domain.Entities;

namespace Domain.Tests;

public sealed class DocumentIngestionLifecycleTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _documentId = Guid.NewGuid();

    [Fact]
    public void Document_initialises_with_Pending_ingestion_status_by_default()
    {
        var document = CreateDocument();

        Assert.Equal(DocumentIngestionStatus.Pending, document.IngestionStatus);
    }

    [Fact]
    public void Document_initialises_with_explicit_ingestion_status_when_provided()
    {
        var document = CreateDocument(DocumentIngestionStatus.Pending);

        Assert.Equal(DocumentIngestionStatus.Pending, document.IngestionStatus);
    }

    [Fact]
    public void Document_rejects_invalid_enum_value_on_construction()
    {
        var invalidStatus = (DocumentIngestionStatus)999;

        Assert.Throws<ArgumentOutOfRangeException>(() => CreateDocument(invalidStatus));
    }

    [Fact]
    public void Transition_from_Pending_to_Completed_via_MarkAsCompleted_succeeds()
    {
        var document = CreateDocument();

        document.MarkAsCompleted();

        Assert.Equal(DocumentIngestionStatus.Completed, document.IngestionStatus);
    }

    [Fact]
    public void Transition_from_Pending_to_Failed_via_MarkAsFailed_succeeds()
    {
        var document = CreateDocument();

        document.MarkAsFailed();

        Assert.Equal(DocumentIngestionStatus.Failed, document.IngestionStatus);
    }

    [Fact]
    public void Transition_from_Pending_to_Completed_via_TransitionTo_succeeds()
    {
        var document = CreateDocument();

        document.TransitionTo(DocumentIngestionStatus.Completed);

        Assert.Equal(DocumentIngestionStatus.Completed, document.IngestionStatus);
    }

    [Fact]
    public void Transition_from_Pending_to_Failed_via_TransitionTo_succeeds()
    {
        var document = CreateDocument();

        document.TransitionTo(DocumentIngestionStatus.Failed);

        Assert.Equal(DocumentIngestionStatus.Failed, document.IngestionStatus);
    }

    [Theory]
    [InlineData(DocumentIngestionStatus.Completed, DocumentIngestionStatus.Pending)]
    [InlineData(DocumentIngestionStatus.Completed, DocumentIngestionStatus.Completed)]
    [InlineData(DocumentIngestionStatus.Completed, DocumentIngestionStatus.Failed)]
    [InlineData(DocumentIngestionStatus.Failed, DocumentIngestionStatus.Pending)]
    [InlineData(DocumentIngestionStatus.Failed, DocumentIngestionStatus.Completed)]
    [InlineData(DocumentIngestionStatus.Failed, DocumentIngestionStatus.Failed)]
    public void Invalid_transitions_from_terminal_states_throw_InvalidOperationException(
        DocumentIngestionStatus initialStatus,
        DocumentIngestionStatus targetStatus)
    {
        var document = CreateDocument();
        if (initialStatus == DocumentIngestionStatus.Completed)
        {
            document.MarkAsCompleted();
        }
        else
        {
            document.MarkAsFailed();
        }

        Assert.Throws<InvalidOperationException>(() => document.TransitionTo(targetStatus));
    }

    [Fact]
    public void TransitionTo_rejects_undefined_enum_value()
    {
        var document = CreateDocument();

        Assert.Throws<ArgumentOutOfRangeException>(() => document.TransitionTo((DocumentIngestionStatus)999));
    }

    private Document CreateDocument(DocumentIngestionStatus status = DocumentIngestionStatus.Pending)
    {
        return new Document(
            _documentId,
            _tenantId,
            "Sample Title",
            "source-ref-123",
            DateTimeOffset.UtcNow,
            status);
    }
}
