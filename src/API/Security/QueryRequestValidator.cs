namespace GovernmentDomainCopilot.API.Security;

public static class QueryRequestValidator
{
    public const string QueryTooLongErrorCode = "QueryTooLong";

    /// <summary>Returns the stable, non-echoing validation response for oversized user text.</summary>
    public static IResult? Validate(string? userText, ApiSecurityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return userText?.Length > options.MaxQueryLength
            ? Results.BadRequest(new { error = QueryTooLongErrorCode })
            : null;
    }
}
