namespace Application.Tests.Evaluation;

using GovernmentDomainCopilot.Application.Answering.Abstractions;
using GovernmentDomainCopilot.Application.Answering.Models;
using GovernmentDomainCopilot.Application.Evaluation.Abstractions;
using GovernmentDomainCopilot.Application.Evaluation.Models;
using GovernmentDomainCopilot.Application.Evaluation.Services;
using Microsoft.Extensions.Logging.Abstractions;

public sealed class EvaluationHarnessTests
{
    private static readonly Guid DefaultTenant = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static EvaluationCase MakeCase(bool expectRefusal = false, bool isAdversarial = false,
        string id = "test-001", string? sourceRef = "ref-proc-101") => new(
        id, DefaultTenant, "Test", "Test query?", expectRefusal,
        expectRefusal ? Array.Empty<string>() : new[] { sourceRef ?? "ref-proc-101" },
        Array.Empty<string>(),
        isAdversarial, isAdversarial ? "PromptInjection" : null);

    private static IGroundedAnswerUseCase FixedGroundedUseCase(GroundedAnswerStatus status, string? sourceRef = "ref-proc-101")
    {
        GroundedAnswerResponse response = status == GroundedAnswerStatus.Grounded
            ? new GroundedAnswerResponse(
                GroundedAnswerStatus.Grounded,
                "Answer text [1].",
                null,
                new[] { new CitationItem("[1]", Guid.NewGuid(), Guid.NewGuid(), sourceRef ?? "ref-proc-101", "Doc", 1) },
                "TestProvider", "test-model", TimeSpan.FromMilliseconds(10))
            : new GroundedAnswerResponse(
                GroundedAnswerStatus.Refused, null, "Insufficient evidence",
                Array.Empty<CitationItem>(), "TestProvider", "test-model", TimeSpan.FromMilliseconds(5));

        return new StubGroundedAnswerUseCase(response);
    }

    [Fact]
    public async Task RunAsync_AllAnswerableCasesGrounded_AllPass()
    {
        var cases = new[] { MakeCase(expectRefusal: false, id: "a-001"), MakeCase(expectRefusal: false, id: "a-002") };
        var harness = BuildHarness(FixedGroundedUseCase(GroundedAnswerStatus.Grounded));

        var report = await harness.RunAsync(cases);

        Assert.Equal(2, report.TotalCases);
        Assert.Equal(2, report.PassedCases);
        Assert.Equal(0, report.FailedCases);
        Assert.Equal(1.0, report.RetrievalHitRate);
        Assert.Equal(1.0, report.GroundednessScore);
        Assert.Equal(1.0, report.RefusalCorrectness);
    }

    [Fact]
    public async Task RunAsync_AllRefusalCasesRefused_AllPass()
    {
        var cases = new[] { MakeCase(expectRefusal: true, id: "r-001"), MakeCase(expectRefusal: true, id: "r-002") };
        var harness = BuildHarness(FixedGroundedUseCase(GroundedAnswerStatus.Refused));

        var report = await harness.RunAsync(cases);

        Assert.Equal(2, report.TotalCases);
        Assert.Equal(2, report.PassedCases);
    }

    [Fact]
    public async Task RunAsync_AdversarialCaseRefused_Passes()
    {
        var cases = new[] { MakeCase(expectRefusal: true, isAdversarial: true, id: "adv-001") };
        var harness = BuildHarness(FixedGroundedUseCase(GroundedAnswerStatus.Refused));

        var report = await harness.RunAsync(cases);

        Assert.Equal(1, report.PassedCases);
        Assert.Equal(0, report.FailedCases);
    }

    [Fact]
    public async Task RunAsync_AdversarialCaseGroundedWhenShouldRefuse_Fails()
    {
        var cases = new[] { MakeCase(expectRefusal: true, isAdversarial: true, id: "adv-001") };
        var harness = BuildHarness(FixedGroundedUseCase(GroundedAnswerStatus.Grounded));

        var report = await harness.RunAsync(cases);

        Assert.Equal(0, report.PassedCases);
        Assert.Equal(1, report.FailedCases);
        Assert.NotNull(report.CaseResults[0].FailureReason);
    }

    [Fact]
    public async Task RunAsync_UseCaseThrowsException_CaseFailsAndDoesNotCountAsRefusal()
    {
        var cases = new[] { MakeCase(expectRefusal: true, id: "err-001") };
        var harness = BuildHarness(new ThrowingGroundedAnswerUseCase());

        var report = await harness.RunAsync(cases);

        Assert.Equal(1, report.TotalCases);
        Assert.Equal(0, report.PassedCases);
        Assert.Equal(1, report.FailedCases);

        var result = report.CaseResults[0];
        Assert.False(result.Passed);
        Assert.False(result.RetrievalHit);
        Assert.False(result.Grounded);
        Assert.False(result.RefusalCorrect);
        Assert.NotNull(result.FailureReason);
        Assert.Contains("Unexpected exception: Simulated use-case failure", result.FailureReason);
    }

    [Fact]
    public async Task RunAsync_ThrowingCase_DoesNotPreventSubsequentCasesFromExecuting()
    {
        var cases = new[]
        {
            MakeCase(expectRefusal: true, id: "throw-001"),
            MakeCase(expectRefusal: true, id: "normal-002")
        };

        var harness = BuildHarness(new FlakyGroundedAnswerUseCase(failFirstCount: 1));

        var report = await harness.RunAsync(cases);

        Assert.Equal(2, report.TotalCases);
        Assert.Equal(1, report.PassedCases);
        Assert.Equal(1, report.FailedCases);

        Assert.False(report.CaseResults[0].Passed);
        Assert.Contains("Unexpected exception", report.CaseResults[0].FailureReason);

        Assert.True(report.CaseResults[1].Passed);
        Assert.True(report.CaseResults[1].RefusalCorrect);
    }

    [Fact]
    public async Task RunAsync_TenantContextUpdatedPerCase()
    {
        var trackingTenantCtx = new TrackingEvaluationTenantContext();
        var differentTenantId = Guid.NewGuid();
        var cases = new[]
        {
            new EvaluationCase("t-001", DefaultTenant, "Cat", "Q?", true, Array.Empty<string>(), Array.Empty<string>(), false),
            new EvaluationCase("t-002", differentTenantId, "Cat", "Q?", true, Array.Empty<string>(), Array.Empty<string>(), false),
        };

        var harness = BuildHarness(
            FixedGroundedUseCase(GroundedAnswerStatus.Refused),
            evalTenantCtx: trackingTenantCtx);

        await harness.RunAsync(cases);

        Assert.Contains(DefaultTenant, trackingTenantCtx.SetTenantIds);
        Assert.Contains(differentTenantId, trackingTenantCtx.SetTenantIds);
    }

    [Fact]
    public async Task RunAsync_EmptyDefaultDataset_UsesLoader()
    {
        var loader = new FixedGoldenDatasetLoader(new[]
        {
            MakeCase(expectRefusal: false, id: "l-001")
        });
        var harness = BuildHarness(FixedGroundedUseCase(GroundedAnswerStatus.Grounded), loader: loader);

        var report = await harness.RunAsync(); // uses default loader

        Assert.Equal(1, report.TotalCases);
    }

    [Fact]
    public async Task RunAsync_IsDeterministic()
    {
        var cases = new[] { MakeCase(expectRefusal: false, id: "d-001") };
        var harness = BuildHarness(FixedGroundedUseCase(GroundedAnswerStatus.Grounded));

        var report1 = await harness.RunAsync(cases);
        var report2 = await harness.RunAsync(cases);

        Assert.Equal(report1.PassedCases, report2.PassedCases);
        Assert.Equal(report1.FailedCases, report2.FailedCases);
        Assert.Equal(report1.RetrievalHitRate, report2.RetrievalHitRate);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static EvaluationHarness BuildHarness(
        IGroundedAnswerUseCase useCase,
        IGoldenDatasetLoader? loader = null,
        TrackingEvaluationTenantContext? evalTenantCtx = null)
    {
        var tenantCtx = evalTenantCtx ?? new TrackingEvaluationTenantContext();
        var goldenLoader = loader ?? new FixedGoldenDatasetLoader(Array.Empty<EvaluationCase>());
        return new EvaluationHarness(
            goldenLoader,
            useCase,
            new EvaluationMetricCalculator(),
            tenantCtx,
            NullLogger<EvaluationHarness>.Instance);
    }

    private sealed class StubGroundedAnswerUseCase(GroundedAnswerResponse response) : IGroundedAnswerUseCase
    {
        public Task<GroundedAnswerResponse> GetGroundedAnswerAsync(
            GovernmentDomainCopilot.Application.Answering.Models.GroundedAnswerRequest request,
            CancellationToken cancellationToken) => Task.FromResult(response);
    }

    private sealed class ThrowingGroundedAnswerUseCase : IGroundedAnswerUseCase
    {
        public Task<GroundedAnswerResponse> GetGroundedAnswerAsync(
            GovernmentDomainCopilot.Application.Answering.Models.GroundedAnswerRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated use-case failure");
    }

    private sealed class FlakyGroundedAnswerUseCase(int failFirstCount) : IGroundedAnswerUseCase
    {
        private int _callCount;

        public Task<GroundedAnswerResponse> GetGroundedAnswerAsync(
            GovernmentDomainCopilot.Application.Answering.Models.GroundedAnswerRequest request,
            CancellationToken cancellationToken)
        {
            if (++_callCount <= failFirstCount)
            {
                throw new InvalidOperationException($"Simulated transient failure on call {_callCount}");
            }

            return Task.FromResult(new GroundedAnswerResponse(
                GroundedAnswerStatus.Refused,
                null,
                "Insufficient evidence",
                Array.Empty<CitationItem>(),
                "TestProvider",
                "test-model",
                TimeSpan.FromMilliseconds(5)));
        }
    }

    private sealed class FixedGoldenDatasetLoader(IReadOnlyList<EvaluationCase> cases) : IGoldenDatasetLoader
    {
        public IReadOnlyList<EvaluationCase> LoadDefault() => cases;
        public IReadOnlyList<EvaluationCase> LoadFromFile(string filePath) => cases;
        public IReadOnlyList<EvaluationCase> LoadFromJson(string json) => cases;
    }

    private sealed class TrackingEvaluationTenantContext : GovernmentDomainCopilot.Application.Evaluation.Abstractions.IEvaluationTenantContext
    {
        private Guid _current = DefaultTenant;
        public List<Guid> SetTenantIds { get; } = new();
        public Guid GetTenantId() => _current;
        public void SetCurrentTenantId(Guid tenantId) { _current = tenantId; SetTenantIds.Add(tenantId); }
    }
}
