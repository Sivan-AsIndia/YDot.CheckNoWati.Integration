namespace YDot.Integration.Domain.Enums;

/// <summary>What the sendTemplateMessages call said about one receiver.</summary>
public enum WatiRequestStatus
{
    /// <summary>Not part of any request yet (or skipped).</summary>
    NotSent,

    /// <summary>WATI accepted the receiver.</summary>
    Accepted,

    /// <summary>WATI refused the receiver (invalid number or parameters).</summary>
    Rejected,

    /// <summary>The request outcome is unknown; being verified by localMessageId.</summary>
    Uncertain
}
