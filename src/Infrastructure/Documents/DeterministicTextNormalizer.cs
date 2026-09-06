using System.Text;

namespace GovernmentDomainCopilot.Infrastructure.Documents;

/// <summary>
/// Provides deterministic text normalisation for plain-text documents prior to chunking.
/// </summary>
public static class DeterministicTextNormalizer
{
    /// <summary>
    /// Normalises source text deterministically by converting line endings to LF (\n),
    /// applying Unicode Normalization Form C (NFC), replacing non-standard whitespace,
    /// and trimming leading/trailing whitespace.
    /// </summary>
    /// <param name="text">Raw source text input.</param>
    /// <returns>Normalised plain text string.</returns>
    public static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        // 1. Normalize line endings (\r\n and \r -> \n)
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');

        // 2. Normalize Unicode (NFC)
        normalized = normalized.Normalize(NormalizationForm.FormC);

        // 3. Replace non-breaking spaces (\u00A0) and zero-width spaces (\u200B)
        normalized = normalized.Replace('\u00A0', ' ').Replace("\u200B", string.Empty);

        // 4. Trim leading and trailing whitespace
        return normalized.Trim();
    }
}
