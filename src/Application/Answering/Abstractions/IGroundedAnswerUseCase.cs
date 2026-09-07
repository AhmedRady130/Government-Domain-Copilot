namespace GovernmentDomainCopilot.Application.Answering.Abstractions;

using GovernmentDomainCopilot.Application.Answering.Models;

public interface IGroundedAnswerUseCase
{
    Task<GroundedAnswerResponse> GetGroundedAnswerAsync(
        GroundedAnswerRequest request,
        CancellationToken cancellationToken);
}
