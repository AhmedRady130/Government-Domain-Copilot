namespace GovernmentDomainCopilot.Application.Sessions.Models;

public sealed class SessionOptions
{
    public const int DefaultMaxMessageContentLength = 16_000;

    /// <summary>
    /// Maximum allowed character length for user and assistant message content in session history.
    /// Messages exceeding this limit will be rejected with a validation error or bounded.
    /// </summary>
    public int MaxMessageContentLength { get; set; } = DefaultMaxMessageContentLength;
}
