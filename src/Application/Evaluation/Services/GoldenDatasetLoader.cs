namespace GovernmentDomainCopilot.Application.Evaluation.Services;

using System.Reflection;
using System.Text.Json;
using GovernmentDomainCopilot.Application.Evaluation.Abstractions;
using GovernmentDomainCopilot.Application.Evaluation.Models;

public sealed class GoldenDatasetLoader : IGoldenDatasetLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public IReadOnlyList<EvaluationCase> LoadDefault()
    {
        var assembly = typeof(GoldenDatasetLoader).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("golden-dataset-v1.json", StringComparison.OrdinalIgnoreCase));

        if (resourceName != null)
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();
                return LoadFromJson(json);
            }
        }

        var searchPaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "data", "golden-dataset-v1.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "data", "golden-dataset-v1.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "data", "golden-dataset-v1.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "data", "golden-dataset-v1.json")
        };

        foreach (var p in searchPaths)
        {
            if (File.Exists(p))
            {
                return LoadFromFile(p);
            }
        }

        throw new FileNotFoundException("Unable to locate default golden dataset resource or file 'golden-dataset-v1.json'.");
    }

    public IReadOnlyList<EvaluationCase> LoadFromFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Golden dataset file not found at: {filePath}", filePath);
        }

        var json = File.ReadAllText(filePath);
        return LoadFromJson(json);
    }

    public IReadOnlyList<EvaluationCase> LoadFromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var cases = JsonSerializer.Deserialize<List<EvaluationCaseDto>>(json, JsonOptions);
        if (cases == null || cases.Count == 0)
        {
            throw new InvalidOperationException("Evaluation dataset is empty or invalid JSON.");
        }

        var result = new List<EvaluationCase>(cases.Count);
        foreach (var c in cases)
        {
            if (string.IsNullOrWhiteSpace(c.Id))
            {
                throw new InvalidOperationException("Evaluation case has missing or empty Id.");
            }
            if (string.IsNullOrWhiteSpace(c.Query))
            {
                throw new InvalidOperationException($"Evaluation case '{c.Id}' has missing or empty Query.");
            }
            if (c.TenantId == Guid.Empty)
            {
                throw new InvalidOperationException($"Evaluation case '{c.Id}' has empty TenantId.");
            }

            result.Add(new EvaluationCase(
                c.Id,
                c.TenantId,
                c.Category ?? "General",
                c.Query,
                c.ExpectRefusal,
                c.ExpectedSourceReferences ?? (IReadOnlyList<string>)Array.Empty<string>(),
                c.ExpectedKeywords ?? (IReadOnlyList<string>)Array.Empty<string>(),
                c.IsAdversarial,
                c.AdversarialType,
                c.Description));
        }

        return result;
    }

    private sealed class EvaluationCaseDto
    {
        public string? Id { get; set; }
        public Guid TenantId { get; set; }
        public string? Category { get; set; }
        public string? Query { get; set; }
        public bool ExpectRefusal { get; set; }
        public List<string>? ExpectedSourceReferences { get; set; }
        public List<string>? ExpectedKeywords { get; set; }
        public bool IsAdversarial { get; set; }
        public string? AdversarialType { get; set; }
        public string? Description { get; set; }
    }
}
