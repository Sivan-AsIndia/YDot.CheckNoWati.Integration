namespace YDot.Integration.Domain.Enums;

/// <summary>The outcome of one sendTemplateMessages call.</summary>
public enum WatiBatchStatus
{
    /// <summary>Created, not sent yet.</summary>
    Pending,

    /// <summary>The HTTP request is in flight. If the worker stops here the batch becomes Uncertain on restart.</summary>
    Sending,

    /// <summary>WATI accepted every receiver.</summary>
    Sent,

    /// <summary>WATI accepted some receivers and rejected others.</summary>
    PartiallyAccepted,

    /// <summary>WATI refused the batch, or every retry failed.</summary>
    Failed,

    /// <summary>The request may have reached WATI; each message is verified by its localMessageId before anything is resent.</summary>
    Uncertain
}
