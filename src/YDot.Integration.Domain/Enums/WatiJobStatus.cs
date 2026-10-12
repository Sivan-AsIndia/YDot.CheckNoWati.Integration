namespace YDot.Integration.Domain.Enums;

/// <summary>The lifecycle of a WATI job.</summary>
public enum WatiJobStatus
{
    /// <summary>Recipients decided; waiting for the dispatcher.</summary>
    Queued,

    /// <summary>Batches are being sent.</summary>
    Sending,

    /// <summary>Every queued message was accepted by WATI (or there was nothing to send).</summary>
    Completed,

    /// <summary>Finished, but at least one message failed.</summary>
    CompletedWithErrors,

    /// <summary>Nothing could be sent (for example WATI refused the API token).</summary>
    Failed,

    /// <summary>Stopped on request before sending.</summary>
    Cancelled
}
