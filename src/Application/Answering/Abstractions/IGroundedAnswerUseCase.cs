namespace GovernmentDomainCopilot.Application.Answering.Abstractions;

using GovernmentDomainCopilot.Application.Answering.Models;
using GovernmentDomainCopilot.Application.Streaming.Abstractions;

public interface IGroundedAnswerUseCase
{
    Task<GroundedAnswerResponse> GetGroundedAnswerAsync(
        GroundedAnswerRequest request,
        CancellationToken cancellationToken);

    Task<GroundedAnswerResponse> GetGroundedAnswerAsync(
        GroundedAnswerRequest request,
        IOrchestrationEventSink? eventSink,
        CancellationToken cancellationToken) => GetGroundedAnswerAsync(request, cancellationToken);
}
